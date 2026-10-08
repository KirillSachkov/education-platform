using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.Core.Features.Plans;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Courses.Queries;

/// <param name="Kind">
///     Опциональный фильтр по типу курса (<c>COURSE</c> / <c>INTENSIVE</c>). Регистр игнорируется.
///     Если не задан — возвращаются курсы обоих типов (UI вкладка «Все»).
/// </param>
public sealed record GetCatalogQuery(string? Cursor, int Limit, string? Search, string? Kind) : IQuery;

public sealed class GetCatalogQueryValidator : AbstractValidator<GetCatalogQuery>
{
    public GetCatalogQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetCatalogQuery.Limit)));

        RuleFor(x => x.Search)
            .MaximumLength(200)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetCatalogQuery.Search)))
            .When(x => x.Search is not null);

        When(x => !string.IsNullOrWhiteSpace(x.Kind), () =>
            RuleFor(x => x.Kind!)
                .Must(k => Enum.TryParse<Domain.Courses.CourseKind>(k, ignoreCase: true, out _))
                .WithError(GeneralErrors.ValueIsInvalid(nameof(GetCatalogQuery.Kind))));
    }
}

public sealed class GetCatalogEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/catalog", async Task<EndpointResult<CursorResponse<CourseCatalogDto>>> (
                    [AsParameters] GetCatalogQuery request,
                    [FromServices] GetCatalogHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(request, cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCatalogHandler : IQueryHandler<CursorResponse<CourseCatalogDto>, GetCatalogQuery>
{
    /// <summary>
    ///     HybridCache tag stamped on every <c>catalog:*</c> page so the catalog can be
    ///     evicted wholesale (keys carry a dynamic cursor/search/kind suffix and can't be
    ///     enumerated). Used by the catalog-listing approval (#569) to surface an approved
    ///     course immediately instead of waiting out the 60s TTL.
    /// </summary>
    public const string CATALOG_CACHE_TAG = "courses-catalog";

    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(60),
        LocalCacheExpiration = TimeSpan.FromSeconds(15),
    };

    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly ICoursePricingClient _coursePricingClient;
    private readonly IAuthorLookupClient _authorLookupClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _userData;
    private readonly HybridCache _cache;
    private readonly ILogger<GetCatalogHandler> _logger;

    public GetCatalogHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        ICoursePricingClient coursePricingClient,
        IAuthorLookupClient authorLookupClient,
        IEntitlementChecker entitlementChecker,
        UserScopedData userData,
        HybridCache cache,
        ILogger<GetCatalogHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _coursePricingClient = coursePricingClient;
        _authorLookupClient = authorLookupClient;
        _entitlementChecker = entitlementChecker;
        _userData = userData;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CursorResponse<CourseCatalogDto>> Handle(
        GetCatalogQuery query, CancellationToken cancellationToken = default)
    {
        string cursorNorm = query.Cursor ?? "none";
        string searchNorm = string.IsNullOrWhiteSpace(query.Search) ? "none" : query.Search;
        string kindNorm = string.IsNullOrWhiteSpace(query.Kind) ? "none" : query.Kind.ToUpperInvariant();
        string cacheKey = $"catalog:{cursorNorm}:{query.Limit}:{searchNorm}:{kindNorm}";

        CursorResponse<CourseCatalogDto> response = await _cache.GetOrCreateAsync(
            cacheKey,
            async ct => await FetchCatalog(query, ct),
            _cacheOptions,
            tags: [CATALOG_CACHE_TAG],
            cancellationToken: cancellationToken);

        // Кеш — user-agnostic (ключ без юзера, шарится между анонимами и всеми
        // юзерами). Поэтому покрытие доступа штампуем per-request ПОСЛЕ кеша, не
        // запекаем внутрь. Аноним / пустая страница → отдаём как есть (IsAccessible=false).
        if (!_userData.IsAuthenticated || response.Items.Count == 0)
            return response;

        List<Guid> courseIds = response.Items.Select(i => i.Id).ToList();
        IReadOnlyDictionary<Guid, AccessDecision> access =
            await _entitlementChecker.CheckAccessBatchAsync(
                _userData.ToAccessSubject(), ResourceTypes.COURSE, courseIds, cancellationToken);

        // Record `with` создаёт копии — НЕ мутируем кешированный (shared) объект.
        List<CourseCatalogDto> enriched = response.Items
            .Select(i => access.TryGetValue(i.Id, out AccessDecision? d) && d.IsGranted
                ? i with { IsAccessible = true }
                : i)
            .ToList();

        return new CursorResponse<CourseCatalogDto>
        {
            Items = enriched,
            NextCursor = response.NextCursor,
            TotalCount = response.TotalCount,
        };
    }

    private async Task<CursorResponse<CourseCatalogDto>> FetchCatalog(
        GetCatalogQuery query, CancellationToken cancellationToken)
    {
        int limit = Math.Clamp(query.Limit, 1, 100);
        SortKeyCursor? cursor = SortKeyCursor.Decode(query.Cursor);

        // has_free_content вычисляется через коррелированный EXISTS — true, если у курса
        // есть хотя бы один опубликованный материал или задание с открытым доступом
        // (PUBLIC / REGISTERED / FREE). Это позволяет фронту показывать badge
        // «Пробный доступ» в каталоге.
        string sql = """
                     SELECT
                         c.id,
                         c.author_id,
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
                     WHERE c.status = 'PUBLISHED'
                       AND c.is_catalog_listed = true
                       AND (@Kind IS NULL OR c.kind = @Kind)
                       AND (@Search IS NULL OR c.title ILIKE '%' || @Search || '%' OR c.description ILIKE '%' || @Search || '%')
                       AND (@CursorSortKey IS NULL OR (c.sort_key, c.id) > (@CursorSortKey, @CursorId))
                     ORDER BY c.sort_key ASC, c.id ASC
                     LIMIT @Limit;
                     """;

        DbConnection connection = _transactionManager.GetDbConnection();

        long totalCount = 0;

        List<CatalogRow> rows = (await connection.QueryAsync<CatalogRow, long, CatalogRow>(
            sql,
            param: new
            {
                Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search,
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

        // Author credit (#569): resolve display name + avatar id per distinct author.
        // CachedAuthorLookupClient soft-degrades to empty on AuthService outage.
        List<Guid> authorIds = rows.Select(r => r.AuthorId).Distinct().ToList();
        Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> authorsResult =
            await _authorLookupClient.GetAuthorsByIdsAsync(authorIds, cancellationToken);
        IReadOnlyDictionary<Guid, AuthorCreditDto> authorsMap = authorsResult.IsSuccess
            ? authorsResult.Value
            : new Dictionary<Guid, AuthorCreditDto>();

        // Merge course-cover ids AND author-avatar ids into ONE FileService batch — no extra
        // round-trip for avatars (resolved in the same GetFilesBatchAsync as covers).
        List<Guid> fileIds = rows
            .Where(r => r.ImageId is not null)
            .Select(r => r.ImageId!.Value)
            .Concat(authorsMap.Values
                .Where(a => a.AvatarId is not null)
                .Select(a => a.AvatarId!.Value))
            .Distinct()
            .ToList();

        Dictionary<Guid, string> fileUrlMap = [];

        if (fileIds.Count > 0)
        {
            var batchResult = await _fileServiceClient.GetFilesBatchAsync(fileIds, cancellationToken);
            if (batchResult is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in batchResult.Value)
                {
                    if (file.ContentUrl is not null)
                        fileUrlMap[file.Id] = file.ContentUrl;
                }
            }
            else
            {
                _logger.LogWarning("Failed to fetch batch images for catalog courses");
            }
        }

        // Batch-fetch per-course pricing from AccessService. CachedCoursePricingClient
        // soft-degrades on outage (returns subset / empty) — catalog never blocks on this.
        List<Guid> courseIds = rows.Select(r => r.Id).ToList();
        Result<IReadOnlyDictionary<Guid, CoursePricingDto>, Error> pricingResult =
            await _coursePricingClient.GetPlansForCoursesAsync(courseIds, cancellationToken);
        IReadOnlyDictionary<Guid, CoursePricingDto> pricingMap = pricingResult.IsSuccess
            ? pricingResult.Value
            : new Dictionary<Guid, CoursePricingDto>();

        // Один снимок времени на батч — все карточки оценивают акцию относительно одного `now`.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<CourseCatalogDto> items = rows
            .Select(row =>
            {
                string? imageUrl =
                    row.ImageId is not null && fileUrlMap.TryGetValue(row.ImageId.Value, out string? url)
                        ? url
                        : null;

                CoursePricingBlock? pricing = null;
                if (pricingMap.TryGetValue(row.Id, out CoursePricingDto? p))
                {
                    bool promoActive = p.IsPromotionActive(now);
                    pricing = new CoursePricingBlock(
                        p.PlanId,
                        p.PriceCents,
                        p.Currency,
                        p.Slug,
                        EffectivePriceCents: p.EffectivePriceCents(now),
                        DiscountPercent: promoActive ? p.DiscountPercent : null,
                        DiscountEndsAt: promoActive ? p.DiscountEndsAt : null,
                        PromotionActive: promoActive);
                }

                string? authorName = null;
                string? authorAvatarUrl = null;
                if (authorsMap.TryGetValue(row.AuthorId, out AuthorCreditDto? author))
                {
                    authorName = author.DisplayName;
                    if (author.AvatarId is { } avatarId
                        && fileUrlMap.TryGetValue(avatarId, out string? avatarUrl))
                    {
                        authorAvatarUrl = avatarUrl;
                    }
                }

                return new CourseCatalogDto(row.Id, row.Slug, row.Title, row.Description, row.Kind, row.ImageId,
                    imageUrl, row.HasFreeContent, row.IsNew, row.CreatedAt, pricing,
                    ShowInFullAccess: row.ShowInFullAccess,
                    AuthorDisplayName: authorName,
                    AuthorAvatarUrl: authorAvatarUrl);
            })
            .ToList();

        string? nextCursor = hasMore
            ? SortKeyCursor.Encode(rows[^1].SortKey, rows[^1].Id)
            : null;

        return new CursorResponse<CourseCatalogDto> { Items = items, NextCursor = nextCursor, TotalCount = totalCount };
    }

    private sealed class CatalogRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
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
