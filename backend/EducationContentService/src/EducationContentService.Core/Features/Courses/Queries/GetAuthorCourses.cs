using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Courses.Queries;

public sealed record GetAuthorCoursesQuery(Guid AuthorId, string? Cursor, int Limit, string? Kind) : IQuery;

public sealed class GetAuthorCoursesQueryValidator : AbstractValidator<GetAuthorCoursesQuery>
{
    public GetAuthorCoursesQueryValidator()
    {
        RuleFor(x => x.AuthorId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetAuthorCoursesQuery.AuthorId)));

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetAuthorCoursesQuery.Limit)));

        When(x => !string.IsNullOrWhiteSpace(x.Kind), () =>
            RuleFor(x => x.Kind!)
                .Must(k => Enum.TryParse<Domain.Courses.CourseKind>(k, ignoreCase: true, out _))
                .WithError(GeneralErrors.ValueIsInvalid(nameof(GetAuthorCoursesQuery.Kind))));
    }
}

public sealed class GetAuthorCoursesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/by-author/{authorId:guid}", async Task<EndpointResult<CursorResponse<CourseCatalogDto>>> (
                    [FromRoute] Guid authorId,
                    [FromQuery] string? cursor,
                    [FromQuery] int? limit,
                    [FromQuery] string? kind,
                    [FromServices] GetAuthorCoursesHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetAuthorCoursesQuery(authorId, cursor, limit ?? 12, kind), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetAuthorCoursesHandler
    : IQueryHandler<CursorResponse<CourseCatalogDto>, GetAuthorCoursesQuery>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(15),
    };

    // Per-author cache tag so moderator approval (SetCatalogListing) can evict this author's
    // whole portfolio immediately (#569) — otherwise an approved course lags up to 60s here.
    public const string AuthorCacheTagPrefix = "by-author-courses";

    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly HybridCache _cache;
    private readonly ILogger<GetAuthorCoursesHandler> _logger;

    public GetAuthorCoursesHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        HybridCache cache,
        ILogger<GetAuthorCoursesHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CursorResponse<CourseCatalogDto>> Handle(
        GetAuthorCoursesQuery query, CancellationToken cancellationToken = default)
    {
        int limit = Math.Clamp(query.Limit, 1, 100);
        string cursorNorm = query.Cursor ?? "none";
        string kindNorm = string.IsNullOrWhiteSpace(query.Kind) ? "none" : query.Kind.ToUpperInvariant();
        string cacheKey = $"by-author:{query.AuthorId}:{cursorNorm}:{limit}:{kindNorm}";

        return await _cache.GetOrCreateAsync(
            cacheKey,
            async ct => await FetchAuthorCourses(query, limit, ct),
            _cacheOptions,
            tags: [$"{AuthorCacheTagPrefix}:{query.AuthorId}"],
            cancellationToken: cancellationToken);
    }

    private async Task<CursorResponse<CourseCatalogDto>> FetchAuthorCourses(
        GetAuthorCoursesQuery query, int limit, CancellationToken cancellationToken)
    {
        SortKeyCursor? cursor = SortKeyCursor.Decode(query.Cursor);

        const string sql = """
                           SELECT
                               c.id,
                               c.slug,
                               c.title,
                               c.description,
                               c.kind,
                               c.image_id,
                               c.is_new,
                               c.show_in_full_access,
                               c.sort_key,
                               c.created_at,
                               (
                                   EXISTS (
                                       SELECT 1
                                       FROM materials m
                                       JOIN module_items mi ON mi.reference_id = m.id AND mi.item_type = 'Material'
                                       JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                                       WHERE ci.course_id = c.id
                                         AND m.access_type IN ('PUBLIC', 'REGISTERED')
                                         AND m.status = 'PUBLISHED'
                                   )
                                   OR EXISTS (
                                       SELECT 1
                                       FROM issues i
                                       JOIN module_items mi ON mi.reference_id = i.id AND mi.item_type = 'Issue'
                                       JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                                       WHERE ci.course_id = c.id AND i.access_type IN ('PUBLIC', 'REGISTERED') AND i.status = 'PUBLISHED'
                                   )
                               ) AS has_free_content,
                               COUNT(*) OVER() AS total_count
                           FROM courses c
                           WHERE c.author_id = @AuthorId
                             AND c.status = 'PUBLISHED'
                             AND c.is_catalog_listed = true
                             AND (@Kind IS NULL OR c.kind = @Kind)
                             AND (@CursorSortKey IS NULL OR (c.sort_key, c.id) > (@CursorSortKey, @CursorId))
                           ORDER BY c.sort_key ASC, c.id ASC
                           LIMIT @Limit;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        long totalCount = 0;

        List<AuthorCourseRow> rows = (await connection.QueryAsync<AuthorCourseRow, long, AuthorCourseRow>(
            sql,
            param: new
            {
                query.AuthorId,
                Kind = string.IsNullOrWhiteSpace(query.Kind) ? null : query.Kind.ToUpperInvariant(),
                CursorSortKey = cursor?.SortKey,
                CursorId = cursor?.LastId,
                Limit = limit + 1
            },
            splitOn: "total_count",
            map: (row, tc) =>
            {
                totalCount = tc;
                return row;
            })).ToList();

        bool hasMore = rows.Count > limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        List<Guid> imageIds = rows
            .Where(r => r.ImageId is not null)
            .Select(r => r.ImageId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> imageUrlMap = [];

        if (imageIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetFilesBatchAsync(imageIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in batchResult.Value)
                {
                    if (file.ContentUrl is not null)
                        imageUrlMap[file.Id] = file.ContentUrl;
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch batch images for author courses");
            }
        }

        List<CourseCatalogDto> items = rows
            .Select(row =>
            {
                string? imageUrl =
                    row.ImageId is not null && imageUrlMap.TryGetValue(row.ImageId.Value, out string? url)
                        ? url
                        : null;

                // AuthorDisplayName / AuthorAvatarUrl intentionally left null (#569): this is a
                // single-author portfolio — the author is already known from the {authorId} route,
                // so per-card bylines would be redundant. The shared CourseCatalogCard renders no
                // byline when the name is null. (Mixed-author surfaces — catalog/detail — populate it.)
                return new CourseCatalogDto(row.Id, row.Slug, row.Title, row.Description, row.Kind, row.ImageId,
                    imageUrl, row.HasFreeContent, row.IsNew, row.CreatedAt,
                    ShowInFullAccess: row.ShowInFullAccess);
            })
            .ToList();

        string? nextCursor = hasMore
            ? SortKeyCursor.Encode(rows[^1].SortKey, rows[^1].Id)
            : null;

        return new CursorResponse<CourseCatalogDto> { Items = items, NextCursor = nextCursor, TotalCount = totalCount };
    }

    private sealed class AuthorCourseRow
    {
        public Guid Id { get; init; }
        public string Slug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public Guid? ImageId { get; init; }
        public bool IsNew { get; init; }
        public bool ShowInFullAccess { get; init; }
        public bool HasFreeContent { get; init; }
        public string SortKey { get; init; } = null!;
        public DateTime CreatedAt { get; init; }
    }
}
