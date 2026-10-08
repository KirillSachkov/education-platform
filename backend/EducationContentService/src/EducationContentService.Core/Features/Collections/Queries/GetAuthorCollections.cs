using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Collections;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Collections.Queries;

public sealed record GetAuthorCollectionsQuery(
    Guid AuthorId, string? Cursor, int Limit = 20) : IQuery;

public sealed class GetAuthorCollectionsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("authors/{authorId:guid}/collections",
                async Task<EndpointResult<CursorResponse<CollectionSummaryDto>>> (
                    [FromRoute] Guid authorId,
                    [FromQuery] string? cursor,
                    [FromQuery] int limit,
                    [FromServices] GetAuthorCollectionsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetAuthorCollectionsQuery(authorId, cursor, limit == 0 ? 20 : limit),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetAuthorCollectionsHandler
    : IQueryHandler<CursorResponse<CollectionSummaryDto>, GetAuthorCollectionsQuery>
{
    private const int MAX_LIMIT = 100;
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly CollectionAccessEnricher _accessEnricher;
    private readonly ILogger<GetAuthorCollectionsHandler> _logger;

    public GetAuthorCollectionsHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        CollectionAccessEnricher accessEnricher,
        ILogger<GetAuthorCollectionsHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _accessEnricher = accessEnricher;
        _logger = logger;
    }

    public async Task<CursorResponse<CollectionSummaryDto>> Handle(
        GetAuthorCollectionsQuery query, CancellationToken cancellationToken = default)
    {
        // Silent clamp вместо FluentValidation — endpoint публичный и анонимный,
        // мусорный ?limit=99999 из адресной строки логичнее молча зажать в [1..100],
        // чем возвращать ValidationError на безобидный query-param. Верхняя граница
        // защищает от DoS через большие лимиты.
        int limit = Math.Clamp(query.Limit, 1, MAX_LIMIT);
        Core.Cursor? cursor = Core.Cursor.Decode(query.Cursor);

        DbConnection connection = _transactionManager.GetDbConnection();

        var parameters = new
        {
            query.AuthorId,
            CursorUpdatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = limit + 1,
        };

        const string sql = """
                            SELECT
                                c.id,
                                c.author_id,
                                c.title,
                                c.description,
                                c.cover_image_id,
                                c.course_id,
                                crs.title AS course_title,
                                crs.slug AS course_slug,
                                c.status,
                                c.access_type,
                                c.is_pinned,
                                c.pinned_sort_key,
                                c.created_at,
                                c.updated_at,
                                COALESCE(ic.item_count, 0) AS item_count,
                                (SELECT COUNT(*)
                                 FROM collections
                                 WHERE author_id = @AuthorId
                                   AND status = 'PUBLISHED') AS total_count
                            FROM collections c
                            LEFT JOIN courses crs ON crs.id = c.course_id
                            LEFT JOIN (
                                SELECT cs.collection_id, COUNT(ci.id) AS item_count
                                FROM collection_sections cs
                                JOIN collection_items ci ON ci.section_id = cs.id
                                GROUP BY cs.collection_id
                            ) ic ON ic.collection_id = c.id
                            WHERE c.author_id = @AuthorId
                              AND c.status = 'PUBLISHED'
                              AND (@CursorUpdatedAt IS NULL OR (c.updated_at, c.id) < (@CursorUpdatedAt, @CursorId))
                            ORDER BY c.updated_at DESC, c.id DESC
                            LIMIT @Limit;
                            """;

        long totalCount = 0;

        List<CollectionSummaryRow> rows = (await connection.QueryAsync<CollectionSummaryRow, long, CollectionSummaryRow>(
            sql: sql,
            map: (row, count) =>
            {
                totalCount = count;
                return row;
            },
            param: parameters,
            splitOn: "total_count")).ToList();

        bool hasMore = rows.Count > limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        Dictionary<Guid, string> imageUrlMap = await ResolveImageUrls(
            rows.Where(r => r.CoverImageId is not null).Select(r => r.CoverImageId!.Value).Distinct().ToList(),
            cancellationToken);

        // Per-item access — нужен enricher'у, чтобы снять замок с карточки если в гейтнутой
        // подборке есть хоть один видимый материал.
        IReadOnlyDictionary<Guid, IReadOnlyList<CollectionItemAccessRow>> itemsByCollection =
            await CollectionItemAccessLoader.LoadAsync(
                connection,
                rows.Select(r => r.Id).ToArray(),
                cancellationToken);

        IReadOnlyList<CollectionAccessResult> accessMarks = await _accessEnricher.EnrichAsync(
            rows.Select(r => new CollectionAccessRow(
                r.Id, r.AuthorId, r.AccessType, r.CourseId,
                itemsByCollection.GetValueOrDefault(r.Id, []))).ToArray(),
            cancellationToken);
        Dictionary<Guid, CollectionAccessResult> accessById = accessMarks.ToDictionary(a => a.CollectionId);

        List<CollectionSummaryDto> items = rows.Select(r =>
        {
            CollectionAccessResult access = accessById.GetValueOrDefault(
                r.Id, new CollectionAccessResult(r.Id, IsAccessible: true, LockReason: null));

            return new CollectionSummaryDto(
                r.Id, r.AuthorId, r.Title, r.Description,
                r.CoverImageId is not null && imageUrlMap.TryGetValue(r.CoverImageId.Value, out string? url) ? url : null,
                r.ItemCount, r.CourseId, r.CourseTitle, r.CourseSlug, r.Status, r.AccessType,
                access.IsAccessible, access.LockReason,
                r.IsPinned, r.PinnedSortKey,
                r.CreatedAt, r.UpdatedAt);
        }).ToList();

        string? nextCursor = hasMore
            ? Core.Cursor.Encode(items[^1].UpdatedAt, items[^1].Id)
            : null;

        return new CursorResponse<CollectionSummaryDto>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount,
        };
    }

    private async Task<Dictionary<Guid, string>> ResolveImageUrls(
        List<Guid> imageIds, CancellationToken cancellationToken)
    {
        Dictionary<Guid, string> map = [];
        if (imageIds.Count == 0)
            return map;

        Result<List<GetFileResponse>?, Error> batchResult =
            await _fileServiceClient.GetFilesBatchAsync(imageIds, cancellationToken);

        if (batchResult is { IsSuccess: true, Value: not null })
        {
            foreach (GetFileResponse file in batchResult.Value)
            {
                if (file.ContentUrl is not null)
                    map[file.Id] = file.ContentUrl;
            }
        }
        else
        {
            _logger.LogWarning("Failed to fetch batch images for author collections");
        }

        return map;
    }

    private sealed class CollectionSummaryRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public Guid? CoverImageId { get; init; }
        public Guid? CourseId { get; init; }
        public string? CourseTitle { get; init; }
        public string? CourseSlug { get; init; }
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public bool IsPinned { get; init; }
        public string? PinnedSortKey { get; init; }
        public int ItemCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
