using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.AuthorCredit;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Courses.Queries;

/// <summary>
///     Admin/moderator moderation queue (issue #569): PUBLISHED courses awaiting
///     catalog-listing approval (<c>is_catalog_listed = false</c>), cursor-paginated by
///     <c>(created_at, id)</c>, enriched with author display name + avatar.
/// </summary>
public sealed record GetPendingCatalogCoursesQuery(string? Cursor, int Limit) : IQuery;

public sealed class GetPendingCatalogCoursesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/admin/pending-listing",
                async Task<EndpointResult<CursorResponse<PendingCatalogCourseDto>>> (
                    [FromQuery] string? cursor,
                    [FromQuery] int? limit,
                    [FromServices] GetPendingCatalogCoursesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetPendingCatalogCoursesQuery(cursor, limit ?? 20), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.MODERATE);
    }
}

public sealed class GetPendingCatalogCoursesHandler
    : IQueryHandler<CursorResponse<PendingCatalogCourseDto>, GetPendingCatalogCoursesQuery>
{
    private const int MAX_LIMIT = 100;

    private readonly ITransactionManager _transactionManager;
    private readonly IAuthorLookupClient _authorLookupClient;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly ILogger<GetPendingCatalogCoursesHandler> _logger;

    public GetPendingCatalogCoursesHandler(
        ITransactionManager transactionManager,
        IAuthorLookupClient authorLookupClient,
        IFileServiceClient fileServiceClient,
        ILogger<GetPendingCatalogCoursesHandler> logger)
    {
        _transactionManager = transactionManager;
        _authorLookupClient = authorLookupClient;
        _fileServiceClient = fileServiceClient;
        _logger = logger;
    }

    public async Task<CursorResponse<PendingCatalogCourseDto>> Handle(
        GetPendingCatalogCoursesQuery query, CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(query.Limit, 1, MAX_LIMIT);
        Cursor? cursor = Cursor.Decode(query.Cursor);

        const string sql = """
                           SELECT
                               c.id,
                               c.author_id,
                               c.slug,
                               c.title,
                               c.description,
                               c.kind,
                               c.image_id,
                               c.created_at,
                               COUNT(*) OVER() AS total_count
                           FROM courses c
                           WHERE c.status = 'PUBLISHED'
                             AND c.is_catalog_listed = false
                             AND (@CursorCreatedAt IS NULL
                                  OR (c.created_at, c.id) < (@CursorCreatedAt, @CursorId))
                           ORDER BY c.created_at DESC, c.id DESC
                           LIMIT @Limit;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        long totalCount = 0;

        List<PendingRow> rows = (await connection.QueryAsync<PendingRow, long, PendingRow>(
            sql,
            param: new
            {
                CursorCreatedAt = cursor?.CreatedAt,
                CursorId = cursor?.LastId,
                Limit = limit + 1,
            },
            splitOn: "total_count",
            map: (row, tc) =>
            {
                totalCount = tc;
                return row;
            })).ToList();

        bool hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        // Author credit — distinct author ids resolved via AuthService (soft-degrades to empty).
        List<Guid> authorIds = rows.Select(r => r.AuthorId).Distinct().ToList();
        Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> authorsResult =
            await _authorLookupClient.GetAuthorsByIdsAsync(authorIds, cancellationToken);
        IReadOnlyDictionary<Guid, AuthorCreditDto> authorsMap = authorsResult.IsSuccess
            ? authorsResult.Value
            : new Dictionary<Guid, AuthorCreditDto>();

        // Single FileService batch for author avatars (course covers are not shown here).
        List<Guid> avatarIds = authorsMap.Values
            .Where(a => a.AvatarId is not null)
            .Select(a => a.AvatarId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> avatarUrlMap = [];
        if (avatarIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetFilesBatchAsync(avatarIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in batchResult.Value)
                {
                    if (file.ContentUrl is not null)
                        avatarUrlMap[file.Id] = file.ContentUrl;
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch batch avatars for pending-listing courses");
            }
        }

        List<PendingCatalogCourseDto> items = rows
            .Select(row =>
            {
                string? authorName = null;
                string? authorAvatarUrl = null;
                if (authorsMap.TryGetValue(row.AuthorId, out AuthorCreditDto? author))
                {
                    authorName = author.DisplayName;
                    if (author.AvatarId is { } avatarId
                        && avatarUrlMap.TryGetValue(avatarId, out string? avatarUrl))
                    {
                        authorAvatarUrl = avatarUrl;
                    }
                }

                return new PendingCatalogCourseDto(
                    row.Id, row.AuthorId, row.Slug, row.Title, row.Description, row.Kind,
                    row.ImageId, row.CreatedAt, authorName, authorAvatarUrl);
            })
            .ToList();

        string? nextCursor = hasMore
            ? Cursor.Encode(rows[^1].CreatedAt, rows[^1].Id)
            : null;

        return new CursorResponse<PendingCatalogCourseDto>
        {
            Items = items, NextCursor = nextCursor, TotalCount = totalCount,
        };
    }

    private sealed class PendingRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Slug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
