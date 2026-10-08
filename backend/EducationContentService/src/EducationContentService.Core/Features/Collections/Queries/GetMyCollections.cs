using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Collections;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.Queries;

public sealed record GetMyCollectionsQuery(
    Guid? CourseId, string? Cursor, int Limit) : IQuery;

public sealed class GetMyCollectionsQueryValidator : AbstractValidator<GetMyCollectionsQuery>
{
    public GetMyCollectionsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetMyCollectionsQuery.Limit)));
    }
}

public sealed class GetMyCollectionsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("collections", async Task<EndpointResult<CursorResponse<CollectionSummaryDto>>> (
                    [FromQuery] Guid? courseId,
                    [FromQuery] string? cursor,
                    [FromQuery] int limit,
                    [FromServices] GetMyCollectionsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetMyCollectionsQuery(courseId, cursor, limit == 0 ? 20 : limit),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class GetMyCollectionsHandler
    : IQueryHandlerWithResult<CursorResponse<CollectionSummaryDto>, GetMyCollectionsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IValidator<GetMyCollectionsQuery> _validator;
    private readonly UserScopedData _userData;
    private readonly ILogger<GetMyCollectionsHandler> _logger;

    public GetMyCollectionsHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        IValidator<GetMyCollectionsQuery> validator,
        UserScopedData userData,
        ILogger<GetMyCollectionsHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _validator = validator;
        _userData = userData;
        _logger = logger;
    }

    public async Task<Result<CursorResponse<CollectionSummaryDto>, Error>> Handle(
        GetMyCollectionsQuery query, CancellationToken cancellationToken = default)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Cursor? cursor = Cursor.Decode(query.Cursor);
        DbConnection connection = _transactionManager.GetDbConnection();

        string courseFilter = query.CourseId.HasValue
            ? "AND course_id = @CourseId"
            : "";

        string countSql = $"""
                           SELECT COUNT(*)
                           FROM collections
                           WHERE author_id = @AuthorId
                             {courseFilter};
                           """;

        string dataSql = $"""
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
                              COALESCE(ic.item_count, 0) AS item_count
                          FROM collections c
                          LEFT JOIN courses crs ON crs.id = c.course_id
                          LEFT JOIN (
                              SELECT cs.collection_id, COUNT(ci.id) AS item_count
                              FROM collection_sections cs
                              JOIN collection_items ci ON ci.section_id = cs.id
                              GROUP BY cs.collection_id
                          ) ic ON ic.collection_id = c.id
                          WHERE c.author_id = @AuthorId
                            {courseFilter}
                            AND (@CursorUpdatedAt IS NULL OR (c.updated_at, c.id) < (@CursorUpdatedAt, @CursorId))
                          ORDER BY c.updated_at DESC, c.id DESC
                          LIMIT @Limit;
                          """;

        var parameters = new
        {
            AuthorId = _userData.UserId,
            query.CourseId,
            CursorUpdatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = query.Limit + 1,
        };

        long totalCount = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        List<CollectionSummaryRow> rows = (await connection.QueryAsync<CollectionSummaryRow>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken))).ToList();

        bool hasMore = rows.Count > query.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        Dictionary<Guid, string> imageUrlMap = await ResolveImageUrls(
            rows.Where(r => r.CoverImageId is not null).Select(r => r.CoverImageId!.Value).Distinct().ToList(),
            cancellationToken);

        List<CollectionSummaryDto> items = rows.Select(r => new CollectionSummaryDto(
            r.Id, r.AuthorId, r.Title, r.Description,
            r.CoverImageId is not null && imageUrlMap.TryGetValue(r.CoverImageId.Value, out string? url) ? url : null,
            r.ItemCount, r.CourseId, r.CourseTitle, r.CourseSlug, r.Status, r.AccessType,
            // Author-scoped endpoint: автор всегда видит свои подборки без lock-иконок.
            IsAccessible: true, LockReason: null,
            r.IsPinned, r.PinnedSortKey,
            r.CreatedAt, r.UpdatedAt)).ToList();

        string? nextCursor = hasMore
            ? Cursor.Encode(items[^1].UpdatedAt, items[^1].Id)
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
            _logger.LogWarning("Failed to fetch batch images for my collections");
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
