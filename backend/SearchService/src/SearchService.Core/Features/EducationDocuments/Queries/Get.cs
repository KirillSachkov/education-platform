using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Common;
using ContentAccess;
using Core.Abstractions;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SearchService.Contracts;
using SearchService.Core.Diagnostics;
using SearchService.Domain;

namespace SearchService.Core.Features.EducationDocuments.Queries;

public sealed class GetDocumentsEndpoint : IEndpoint
{
    // Keep the string constant inline here to avoid a Web-layer dependency from Core.
    // Mirror of SearchRateLimiting.SEARCH_PUBLIC_POLICY.
    public const string SEARCH_PUBLIC_RATE_LIMIT_POLICY = "search-public";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet(
                "/search",
                async Task<EndpointResult<SearchResponse<EducationDocumentDto>>>(
                    [AsParameters] GetDocumentsRequest request,
                    [FromServices] GetDocumentsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new GetDocumentsQuery(
                            new SearchRequest(
                                request.Search ?? string.Empty,
                                request.Page ?? 1,
                                request.PageSize ?? 20,
                                request.CourseId,
                                request.TagIds,
                                request.EntityTypes,
                                request.AuthorId,
                                request.Cursor,
                                request.MaterialKind,
                                request.AccessFilter)),
                        cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting(SEARCH_PUBLIC_RATE_LIMIT_POLICY);
    }
}

public sealed record GetDocumentsQuery : IQuery
{
    public GetDocumentsQuery(SearchRequest request)
    {
        Request = request;
    }

    public SearchRequest Request { get; init; }
}

public sealed class SearchCommandValidator : AbstractValidator<GetDocumentsQuery>
{
    public SearchCommandValidator()
    {
        RuleFor(x => x.Request.Search)
            .Must((query, search) =>
                !string.IsNullOrWhiteSpace(search) ||
                query.Request.CourseId.HasValue ||
                query.Request.AuthorId.HasValue ||
                query.Request.TagIds.Length > 0 ||
                query.Request.EntityTypes.Length > 0)
            .WithError(Error.Validation(
                "search.query.empty",
                "Поисковый запрос или хотя бы один фильтр обязателен"));

        When(
            static query => !string.IsNullOrWhiteSpace(query.Request.Search),
            () =>
            {
                RuleFor(x => x.Request.Search)
                    .Must(static query => !query.Trim().All(static ch => ch == '*'))
                    .WithError(Error.Validation(
                        "search.query.wildcard.invalid",
                        "Wildcard-only query is not allowed"));
            });

        RuleFor(x => x.Request.Page)
            .GreaterThan(0)
            .WithError(Error.Validation("search.query.page.invalid", "Номер страницы должен быть больше нуля"));

        RuleFor(x => x.Request.PageSize)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MAX_PAGE_SIZE)
            .WithError(Error.Validation(
                "search.query.page_size.invalid",
                $"Размер страницы должен быть от {Constants.MIN_PAGE_SIZE} до {Constants.MAX_PAGE_SIZE}"));

        RuleFor(x => x.Request)
            .Must(static request => request.Page > 0
                && request.PageSize > 0
                && (long)(request.Page - 1) * request.PageSize < Constants.MAX_RESULT_WINDOW)
            .WithError(Error.Validation(
                "search.query.result_window.exceeded",
                $"Смещение страницы должно быть меньше {Constants.MAX_RESULT_WINDOW}; для глубокой выдачи используйте курсор"));

        RuleFor(x => x.Request.Search)
            .MaximumLength(Constants.MAX_QUERY_LENGTH)
            .WithError(Error.Validation(
                "search.query.too_long",
                $"Поисковый запрос не может быть длиннее {Constants.MAX_QUERY_LENGTH} символов"));

        RuleFor(x => x.Request.TagIds)
            .Must(static values => values.Length <= Constants.MAX_FILTER_VALUES)
            .WithError(Error.Validation(
                "search.query.too_many_tags",
                $"Фильтр не может содержать больше {Constants.MAX_FILTER_VALUES} тегов"));

        RuleFor(x => x.Request.EntityTypes)
            .Must(static values => values.Length <= Constants.SEARCHABLE_ENTITY_TYPES.Count)
            .WithError(Error.Validation(
                "search.query.too_many_entity_types",
                "Фильтр содержит слишком много типов сущностей"));

        RuleFor(x => x.Request.EntityTypes)
            .Must(static values => values.All(Constants.SEARCHABLE_ENTITY_TYPES.Contains))
            .WithError(Error.Validation(
                "search.query.entity_type.unsupported",
                "Фильтр содержит неподдерживаемый тип сущности"));

        When(
            static query => string.IsNullOrWhiteSpace(query.Request.Search)
                && !string.IsNullOrWhiteSpace(query.Request.Cursor),
            () => RuleFor(x => x.Request.Cursor!)
                .Must(static cursor => SearchCursor.Decode(cursor) is { UpdatedAtTicks: > 0 })
                .WithError(Error.Validation(
                    "search.query.cursor.invalid",
                    "Некорректный курсор поиска")));

        When(
            static query => !string.IsNullOrWhiteSpace(query.Request.AccessFilter),
            () => RuleFor(x => x.Request.AccessFilter!)
                .Must(static value =>
                    value.Equals("free", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("public", StringComparison.OrdinalIgnoreCase))
                .WithError(Error.Validation(
                    "search.query.access_filter.invalid",
                    "Неподдерживаемый фильтр доступа")));

        // material_kind подставляется в Typesense filter_by как free-string (BuildFilter →
        // `material_kind:=`{value}``). Allowlist-валидация закрывает filter injection: без неё
        // значение с backtick + `||` могло бы дописать `is_deleted:=true` и раскрыть
        // soft-deleted/draft-документы. Прочие параметры фильтра — Guid/enum/fixed-tag, не free.
        When(
            static query => !string.IsNullOrWhiteSpace(query.Request.MaterialKind),
            () =>
            {
                RuleFor(x => x.Request.MaterialKind!)
                    .Must(static kind => Constants.ALLOWED_MATERIAL_KINDS.Contains(kind))
                    .WithError(Error.Validation(
                        "search.query.material_kind.invalid",
                        "Недопустимый тип материала"));
            });
    }
}

public sealed class GetDocumentsHandler : IQueryHandlerWithResult<SearchResponse<EducationDocumentDto>, GetDocumentsQuery>
{
    // Поля, по которым ищем. content добавлен как полнотекстовый источник (материалы),
    // но имеет минимальный вес, чтобы совпадение в title/description оставалось в топе.
    // chapter_titles — заголовки глав видео (Kinescope chapters); средний вес — выше
    // content (более структурированный сигнал «главы лекции»), но ниже title/description.
    private const string SEARCH_FIELDS = "title,description,chapter_titles,content";
    private const string FACET_FIELDS = "entity_type,tag_ids";
    private const string QUERY_BY_WEIGHTS = "10,4,3,1";

    // content НЕ возвращается в API. Highlight по content нужен только для документов,
    // доступных текущему пользователю; для locked/unenrolled документов он вычищается
    // перед ответом, чтобы тело материала не утекало через сниппеты.
    private const string EXCLUDE_FIELDS = "content";
    private const string HIGHLIGHT_FIELDS = "title,description,chapter_titles,content";
    private const string HIGHLIGHT_FULL_FIELDS = "title,description,chapter_titles";
    private const int SNIPPET_THRESHOLD = 30;

    // Кэшируем результаты только для анонимных вызовов — у них access-фильтр
    // не зависит от UserId/Roles, так что keyspace ограничен параметрами запроса.
    // Авторизованные пользователи мимо кэша: TRIAL/STANDARD enrollments + per-user
    // access tags → key space взорвался бы до миллионов записей.
    // Admin также не кэшируется (draft/deleted контент меняется часто).
    private static readonly HybridCacheEntryOptions _anonCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(5),
    };

    // Короче TTL для error responses — иначе один Typesense-blip раздаёт
    // failure 30s всем анонимным запросам с тем же ключом и подавляет recovery.
    private static readonly HybridCacheEntryOptions _anonErrorCacheOptions = new()
    {
        Expiration = TimeSpan.FromSeconds(5),
        LocalCacheExpiration = TimeSpan.FromSeconds(2),
    };

    private readonly EducationDocumentService _educationDocumentService;
    private readonly IValidator<GetDocumentsQuery> _validator;
    private readonly EducationSearchAccessFilterBuilder _accessFilterBuilder;
    private readonly UserScopedData _user;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly HybridCache _cache;
    private readonly ILogger<GetDocumentsHandler> _logger;

    public GetDocumentsHandler(
        EducationDocumentService educationDocumentService,
        IValidator<GetDocumentsQuery> validator,
        EducationSearchAccessFilterBuilder accessFilterBuilder,
        UserScopedData user,
        IFileServiceClient fileServiceClient,
        HybridCache cache,
        ILogger<GetDocumentsHandler> logger)
    {
        _educationDocumentService = educationDocumentService;
        _validator = validator;
        _accessFilterBuilder = accessFilterBuilder;
        _user = user;
        _fileServiceClient = fileServiceClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<SearchResponse<EducationDocumentDto>, Error>> Handle(
        GetDocumentsQuery query,
        CancellationToken cancellationToken = default)
    {
        string scope = _user.IsAdmin ? "admin" : _user.IsAuthenticated ? "authed" : "anon";

        using Activity? activity = SearchDiagnostics.ActivitySource.StartActivity(
            "search.query", ActivityKind.Server);
        activity?.SetTag("search.scope", scope);
        activity?.SetTag("search.has_text", !string.IsNullOrWhiteSpace(query.Request.Search));
        activity?.SetTag("search.page_size", query.Request.PageSize);
        if (query.Request.CourseId.HasValue)
            activity?.SetTag("search.course_id", query.Request.CourseId.Value.ToString());

        // Cursor is already the page boundary. Applying a page offset on top of it
        // skips valid hits, so the documented "Page is ignored" contract is enforced
        // before validation for both cached anonymous and authenticated paths.
        query = NormalizeBrowseCursorPage(query);

        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Cache-path только для анонимов. Авторизованные и admin идут напрямую
        // (per-user фильтрация разнесёт keyspace; admin видит draft/deleted,
        // требующие freshness). Cache-miss'ы фиксируют Typesense-ошибки как
        // sentinel-result и возвращают Error из Handle.
        if (!_user.IsAuthenticated)
        {
            string cacheKey = BuildAnonymousCacheKey(query.Request);
            bool freshFromUpstream = false;
            CachedAnonymousSearchResult cached = await _cache.GetOrCreateAsync(
                cacheKey,
                async ct =>
                {
                    freshFromUpstream = true;
                    return await ExecuteAnonymousAsync(query, ct);
                },
                _anonCacheOptions,
                cancellationToken: cancellationToken);

            // HybridCache принимает options upfront, поэтому success-кейс уже
            // лёг на 30s. Если результат свежий и содержит Typesense-ошибку —
            // переписываем запись с коротким TTL, чтобы один blip не раздавал
            // failure всем анонимам полминуты и не подавлял recovery.
            if (freshFromUpstream && cached.Error is not null)
            {
                await _cache.SetAsync(
                    cacheKey, cached, _anonErrorCacheOptions, cancellationToken: cancellationToken);
            }

            if (cached.Error is not null)
            {
                activity?.SetStatus(ActivityStatusCode.Error, cached.Error.GetMessage());
                return cached.Error;
            }

            SearchResponse<EducationDocumentDto> response = cached.Response!;
            activity?.SetTag("search.results_count", response.Hits.Count);
            activity?.SetTag("search.total_count", response.TotalCount);
            return response;
        }

        SearchAccessContext accessContext =
            await _accessFilterBuilder.BuildAsync(query.Request.CourseId, cancellationToken);

        // Browse-режим (search пустой) использует cursor-пагинацию: первая страница
        // идёт без cursor-фильтра, следующие — с boundary `updated_at_ticks:<N`.
        // Relevance-режим (search непустой) ranking по _text_match не монотонный —
        // keyset-пагинация невозможна, используем page-based. Если cursor передан
        // вместе с search — игнорируем cursor как not applicable.
        bool isBrowseMode = string.IsNullOrWhiteSpace(query.Request.Search);
        SearchCursor? cursor = isBrowseMode ? SearchCursor.Decode(query.Request.Cursor) : null;

        string filter = BuildFilter(
            accessContext.TypesenseFilter,
            query.Request.TagIds,
            query.Request.EntityTypes,
            query.Request.AuthorId,
            cursor,
            query.Request.MaterialKind,
            query.Request.AccessFilter);

        // В browse-режиме сортируем по updated_at_ticks:desc. Строковый entity_id нельзя
        // использовать в range-boundary Typesense, поэтому lossless UUID-tiebreaker требует
        // новых числовых полей и двухфазного schema rollout. До реализации #757 равные
        // timestamps на границе страницы остаются известным ограничением.
        string? sortBy = isBrowseMode ? "updated_at_ticks:desc" : null;

        Result<SearchResponse<EducationDocument>, Error> searchResult =
            await _educationDocumentService.SearchAsync(
                query.Request,
                SEARCH_FIELDS,
                filter,
                FACET_FIELDS,
                queryByWeights: QUERY_BY_WEIGHTS,
                excludeFields: EXCLUDE_FIELDS,
                highlightFields: HIGHLIGHT_FIELDS,
                highlightFullFields: HIGHLIGHT_FULL_FIELDS,
                snippetThreshold: SNIPPET_THRESHOLD,
                sortBy: sortBy,
                cancellationToken: cancellationToken);

        if (searchResult.IsFailure)
        {
            activity?.SetStatus(ActivityStatusCode.Error, searchResult.Error.ToString());
            return searchResult.Error;
        }

        SearchResponse<EducationDocument> searchResponse = searchResult.Value;
        activity?.SetTag("search.results_count", searchResponse.Hits.Count);
        activity?.SetTag("search.total_count", searchResponse.TotalCount);

        // Batch-резолвим Kinescope thumbnails для всех VIDEO-материалов без кастомной
        // обложки — FileService вернёт poster URL. У материалов, где imageId есть, не
        // трогаем (фронт покажет обложку). Один round-trip на весь page результатов.
        Dictionary<Guid, string> videoThumbMap = await ResolveVideoThumbnailsAsync(
            searchResponse.Hits, cancellationToken);

        // В browse-режиме строим nextCursor из последнего хита. Возвращаем null, если
        // результатов меньше pageSize — значит достигнут конец выдачи.
        string? nextCursor = null;
        if (isBrowseMode
            && searchResponse.Hits.Count > 0
            && searchResponse.Hits.Count < searchResponse.TotalCount)
        {
            EducationDocument last = searchResponse.Hits[^1].Document;
            nextCursor = SearchCursor.Encode(last.UpdatedAtTicks);
        }

        return new SearchResponse<EducationDocumentDto>(
            searchResponse.Hits
                .Select(hit =>
                {
                    AccessLockResult access = accessContext.IsAdmin
                        ? new AccessLockResult(IsAccessible: true, LockReason: null)
                        : LockReasonResolver.Resolve(
                            hit.Document.RequiredAccessTags,
                            accessContext.UserGrants,
                            _user.IsAuthenticated);

                    return MapHit(hit, access, videoThumbMap);
                })
                .ToList(),
            searchResponse.Facets,
            searchResponse.TotalCount,
            searchResponse.Page,
            searchResponse.PageSize,
            nextCursor);
    }

    private static SearchHit<EducationDocumentDto> MapHit(
        SearchHit<EducationDocument> hit,
        AccessLockResult access,
        IReadOnlyDictionary<Guid, string> videoThumbMap)
    {
        string? videoThumbUrl = null;
        if (hit.Document.VideoId is { } videoId)
        {
            videoThumbMap.TryGetValue(videoId, out videoThumbUrl);
        }

        return new SearchHit<EducationDocumentDto>(
            new EducationDocumentDto
            {
                EntityId = hit.Document.EntityId,
                EntityType = hit.Document.EntityType,
                Title = hit.Document.Title,
                Description = hit.Document.Description,
                ImageId = hit.Document.ImageId,
                CourseId = hit.Document.CourseId,
                CourseSlug = hit.Document.CourseSlug,
                CourseTitle = hit.Document.CourseTitle,
                AuthorId = hit.Document.AuthorId,
                ProjectId = hit.Document.ProjectId,
                ProjectTitle = hit.Document.ProjectTitle,
                ModuleId = hit.Document.ModuleId,
                ModuleTitle = hit.Document.ModuleTitle,
                TagIds = hit.Document.TagIds,
                TagTitles = hit.Document.TagTitles,
                UpdatedAtUtc = new DateTime(hit.Document.UpdatedAtTicks, DateTimeKind.Utc),
                IsAccessible = access.IsAccessible,
                LockReason = access.LockReason,
                MaterialKind = hit.Document.MaterialKind,
                VideoThumbnailUrl = videoThumbUrl,
                ChapterTitles = hit.Document.ChapterTitles,
                ChapterTimestamps = hit.Document.ChapterTimestamps,
            },
            hit.Score,
            FilterHighlights(hit.Highlights, access.IsAccessible));
    }

    /// <summary>
    /// Исполняет полный search-pipeline для анонимного вызова. Не вызывает
    /// метрики/активити — их запишет внешний Handle после cache-hit/miss.
    /// Возвращает wrapper-тип (а не Result), чтобы HybridCache мог сериализовать
    /// как success, так и Typesense-ошибку.
    /// </summary>
    private async Task<CachedAnonymousSearchResult> ExecuteAnonymousAsync(
        GetDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        SearchAccessContext accessContext =
            await _accessFilterBuilder.BuildAsync(query.Request.CourseId, cancellationToken);

        bool isBrowseMode = string.IsNullOrWhiteSpace(query.Request.Search);
        SearchCursor? cursor = isBrowseMode ? SearchCursor.Decode(query.Request.Cursor) : null;

        string filter = BuildFilter(
            accessContext.TypesenseFilter,
            query.Request.TagIds,
            query.Request.EntityTypes,
            query.Request.AuthorId,
            cursor,
            query.Request.MaterialKind,
            query.Request.AccessFilter);

        string? sortBy = isBrowseMode ? "updated_at_ticks:desc" : null;

        Result<SearchResponse<EducationDocument>, Error> searchResult =
            await _educationDocumentService.SearchAsync(
                query.Request,
                SEARCH_FIELDS,
                filter,
                FACET_FIELDS,
                queryByWeights: QUERY_BY_WEIGHTS,
                excludeFields: EXCLUDE_FIELDS,
                highlightFields: HIGHLIGHT_FIELDS,
                highlightFullFields: HIGHLIGHT_FULL_FIELDS,
                snippetThreshold: SNIPPET_THRESHOLD,
                sortBy: sortBy,
                cancellationToken: cancellationToken);

        if (searchResult.IsFailure)
        {
            return new CachedAnonymousSearchResult(Response: null, Error: searchResult.Error);
        }

        SearchResponse<EducationDocument> searchResponse = searchResult.Value;

        Dictionary<Guid, string> videoThumbMap = await ResolveVideoThumbnailsAsync(
            searchResponse.Hits, cancellationToken);

        string? nextCursor = null;
        if (isBrowseMode
            && searchResponse.Hits.Count > 0
            && searchResponse.Hits.Count < searchResponse.TotalCount)
        {
            EducationDocument last = searchResponse.Hits[^1].Document;
            nextCursor = SearchCursor.Encode(last.UpdatedAtTicks);
        }

        SearchResponse<EducationDocumentDto> mapped = new(
            searchResponse.Hits
                .Select(hit =>
                {
                    // Анонимный путь: access-фильтр всегда видит IsAuthenticated=false
                    // и пустые UserGrants. Результат LockReasonResolver стабилен и
                    // безопасно кэшируется.
                    AccessLockResult access = LockReasonResolver.Resolve(
                        hit.Document.RequiredAccessTags,
                        accessContext.UserGrants,
                        isAuthenticated: false);

                    return MapHit(hit, access, videoThumbMap);
                })
                .ToList(),
            searchResponse.Facets,
            searchResponse.TotalCount,
            searchResponse.Page,
            searchResponse.PageSize,
            nextCursor);

        return new CachedAnonymousSearchResult(mapped, Error: null);
    }

    /// <summary>
    /// Стабильный ключ кэша на основе нормализованных параметров запроса.
    /// Формат: "search:anon:v1:<sha256-16bytes-hex>". Используем SHA256 сокращённый
    /// до 32 hex-символов — столкновений практически нет, ключ компактный.
    /// </summary>
    private static string BuildAnonymousCacheKey(SearchRequest request)
    {
        string searchNorm = string.IsNullOrWhiteSpace(request.Search)
            ? string.Empty
            : request.Search.Trim().ToLowerInvariant();

        string tagIds = request.TagIds.Length == 0
            ? string.Empty
            : string.Join(",", request.TagIds
                .Distinct()
                .OrderBy(static g => g)
                .Select(static g => g.ToString("N")));

        string entityTypes = request.EntityTypes.Length == 0
            ? string.Empty
            : string.Join(",", request.EntityTypes
                .Distinct()
                .OrderBy(static e => (int)e)
                .Select(static e => ((int)e).ToString(CultureInfo.InvariantCulture)));

        string raw = string.Join(
            "|",
            searchNorm,
            request.Page.ToString(CultureInfo.InvariantCulture),
            request.PageSize.ToString(CultureInfo.InvariantCulture),
            request.CourseId?.ToString("N") ?? string.Empty,
            request.AuthorId?.ToString("N") ?? string.Empty,
            tagIds,
            entityTypes,
            request.Cursor ?? string.Empty,
            request.MaterialKind ?? string.Empty,
            request.AccessFilter?.ToLowerInvariant() ?? string.Empty);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        // v2 = добавили accessFilter в keyspace (issue #242).
        return $"search:anon:v2:{Convert.ToHexString(hash.AsSpan(0, 16))}";
    }

    private static GetDocumentsQuery NormalizeBrowseCursorPage(GetDocumentsQuery query)
    {
        SearchRequest request = query.Request;
        if (!string.IsNullOrWhiteSpace(request.Search)
            || string.IsNullOrWhiteSpace(request.Cursor)
            || request.Page == 1)
        {
            return query;
        }

        return new GetDocumentsQuery(new SearchRequest(
            request.Search,
            1,
            request.PageSize,
            request.CourseId,
            request.TagIds,
            request.EntityTypes,
            request.AuthorId,
            request.Cursor,
            request.MaterialKind,
            request.AccessFilter));
    }

    private static IReadOnlyList<SearchHighlight> FilterHighlights(
        IReadOnlyList<SearchHighlight> highlights,
        bool isAccessible)
    {
        if (isAccessible)
            return highlights;

        return highlights
            .Where(static highlight => !string.Equals(highlight.Field, "content", StringComparison.Ordinal))
            .ToArray();
    }

    /// <summary>
    /// Обёртка для HybridCache: HybridCache сериализует успешный ответ или
    /// Typesense-ошибку (чтобы ошибки тоже подчинялись TTL и не били по шторму).
    /// </summary>
    public sealed record CachedAnonymousSearchResult(
        SearchResponse<EducationDocumentDto>? Response,
        Error? Error);

    private async Task<Dictionary<Guid, string>> ResolveVideoThumbnailsAsync(
        IReadOnlyList<SearchHit<EducationDocument>> hits,
        CancellationToken cancellationToken)
    {
        List<Guid> videoIds = hits
            .Select(h => h.Document.VideoId)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, string> map = [];
        if (videoIds.Count == 0)
            return map;

        Result<List<GetPublicVideoResponse>?, Error> batch =
            await _fileServiceClient.GetVideosBatchAsync(videoIds, cancellationToken);

        if (batch is not { IsSuccess: true, Value: { } videos })
        {
            _logger.LogWarning("Failed to fetch video thumbnails for search hits: {VideoCount} ids", videoIds.Count);
            return map;
        }

        foreach (GetPublicVideoResponse video in videos)
        {
            if (!string.IsNullOrWhiteSpace(video.ThumbnailUrl))
                map[video.Id] = video.ThumbnailUrl!;
        }

        return map;
    }

    private static string BuildFilter(
        string scopeFilter,
        IReadOnlyList<Guid> tagIds,
        IReadOnlyList<EntityType> entityTypes,
        Guid? authorId,
        SearchCursor? cursor,
        string? materialKind,
        string? accessFilter)
    {
        List<string> filters = [];

        if (!string.IsNullOrWhiteSpace(scopeFilter))
        {
            filters.Add(scopeFilter);
        }

        if (authorId.HasValue)
        {
            filters.Add($"author_id:=`{authorId.Value:D}`");
        }

        string tagFilter = BuildTagFilter(tagIds);
        if (!string.IsNullOrWhiteSpace(tagFilter))
        {
            filters.Add(tagFilter);
        }

        string entityTypeFilter = BuildEntityTypeFilter(entityTypes);
        if (!string.IsNullOrWhiteSpace(entityTypeFilter))
        {
            filters.Add(entityTypeFilter);
        }

        if (!string.IsNullOrWhiteSpace(materialKind))
        {
            filters.Add($"material_kind:=`{materialKind}`");
        }

        string accessTypeFilter = BuildAccessTypeFilter(accessFilter, authorId);
        if (!string.IsNullOrWhiteSpace(accessTypeFilter))
        {
            filters.Add(accessTypeFilter);
        }

        string cursorFilter = BuildCursorFilter(cursor);
        if (!string.IsNullOrWhiteSpace(cursorFilter))
        {
            filters.Add(cursorFilter);
        }

        return string.Join(" && ", filters);
    }

    /// <summary>
    /// Когда <paramref name="accessFilter"/> равен <c>"free"</c>, режем выдачу до тех
    /// документов, которые <b>автор пометил как бесплатные</b> — то есть с
    /// AccessType ∈ {PUBLIC, REGISTERED} (зеркалит
    /// <c>AccessFilterTypes.Free</c> в ECS). Дискриминатор по
    /// <c>required_access_tags</c>: <c>access:public</c> (PUBLIC), <c>authenticated</c>
    /// (REGISTERED).
    /// <para>
    /// Issue #358: AccessType.FREE удалён; бесплатный = REGISTERED (system default).
    /// До этого фильтр также включал <c>plan:free:author_X</c> для FREE-материалов.
    /// </para>
    /// <para>
    /// Фильтр <b>не зависит</b> от grants вызывающего — UI на <c>/@slug/knowledge-base</c>
    /// должен показывать одинаковый набор «бесплатного» аноним и залогиненный без планов,
    /// помечая недоступное замком через <c>LockReasonResolver</c>. До issue #255 фильтр
    /// строился из <c>UserGrants</c> и фактически = «доступно мне» — баг.
    /// </para>
    /// <para>
    /// Admin <b>не</b> бэйпасит фильтр — это UI-фильтр выдачи, не access-check. Toggle
    /// «Только бесплатное» обязан фильтровать выдачу одинаково для всех ролей; admin
    /// и так видит lock-замки нет через `LockReasonResolver`, отдельный shortcut здесь
    /// возвращал бы всю выдачу и ломал semantics (issue #279).
    /// </para>
    /// <para>
    /// <c>"public"</c> — более строгий acquisition-фильтр: только
    /// <c>access:public</c>, то есть материалы, которые аноним может открыть без login.
    /// Он не меняет access-check detail endpoint и не расширяет права.
    /// </para>
    /// </summary>
    private static string BuildAccessTypeFilter(
        string? accessFilter,
        Guid? authorId)
    {
        _ = authorId; // parameter retained for caller compatibility — FREE-tag больше не строится
        if (string.IsNullOrWhiteSpace(accessFilter))
        {
            return string.Empty;
        }

        string[] tags = accessFilter.Equals("public", StringComparison.OrdinalIgnoreCase)
            ? [GrantTags.PUBLIC]
            : [GrantTags.PUBLIC, GrantTags.AUTHENTICATED];
        string values = string.Join(",", tags.Select(static tag => $"`{tag}`"));
        return $"required_access_tags:=[{values}]";
    }

    /// <summary>
    /// Keyset boundary для cursor-пагинации. До двухфазного schema rollout из #757
    /// текущий `updated_at_ticks:&lt;N` может пропустить документы с тем же ticks на границе.
    /// </summary>
    private static string BuildCursorFilter(SearchCursor? cursor)
    {
        if (cursor is null)
        {
            return string.Empty;
        }

        return $"updated_at_ticks:<{cursor.UpdatedAtTicks}";
    }

    private static string BuildTagFilter(IReadOnlyList<Guid> tagIds)
    {
        Guid[] selectedTagIds = tagIds
            .Distinct()
            .ToArray();

        if (selectedTagIds.Length == 0)
        {
            return string.Empty;
        }

        string tagValues = string.Join(",", selectedTagIds.Select(static id => $"`{id:D}`"));
        return $"tag_ids:=[{tagValues}]";
    }

    private static string BuildEntityTypeFilter(IReadOnlyList<EntityType> entityTypes)
    {
        EntityType[] selectedEntityTypes = entityTypes
            .Distinct()
            .ToArray();

        if (selectedEntityTypes.Length == 0)
        {
            return string.Empty;
        }

        string values = string.Join(",", selectedEntityTypes.Select(static entityType => $"`{entityType}`"));
        return $"entity_type:=[{values}]";
    }
}
