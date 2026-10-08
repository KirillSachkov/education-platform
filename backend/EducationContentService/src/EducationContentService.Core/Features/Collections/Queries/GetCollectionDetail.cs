using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Collections;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Features.ContentAccess;
using EducationContentService.Core.Features.Materials.Queries;
using EducationContentService.Domain;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using ProgressService.Contracts.HttpCommunication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.Queries;

public sealed record GetCollectionDetailQuery(Guid CollectionId) : IQuery;

public sealed class GetCollectionDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("collections/{collectionId:guid}/detail", async Task<EndpointResult<CollectionDetailDto>> (
                    [FromRoute] Guid collectionId,
                    [FromServices] GetCollectionDetailHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCollectionDetailQuery(collectionId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCollectionDetailHandler
    : IQueryHandlerWithResult<CollectionDetailDto, GetCollectionDetailQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IProgressServiceClient _progressServiceClient;
    private readonly IEntitlementReader _entitlementReader;
    private readonly UserScopedData _userData;
    private readonly ILogger<GetCollectionDetailHandler> _logger;

    public GetCollectionDetailHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient,
        IEntitlementReader entitlementReader,
        UserScopedData userData,
        ILogger<GetCollectionDetailHandler> logger)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _progressServiceClient = progressServiceClient;
        _entitlementReader = entitlementReader;
        _userData = userData;
        _logger = logger;
    }

    public async Task<Result<CollectionDetailDto, Error>> Handle(
        GetCollectionDetailQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // 1. Fetch collection header.
        // WHERE-branch `OR c.author_id = @UserId` сознательно отдаёт DRAFT подборки автору:
        // автор должен иметь preview своих черновиков в editor'е. Зеркалит поведение
        // GetMaterialDetail. Для анонимов UserId = Guid.Empty → branch всегда false.
        const string collectionSql = """
                                     SELECT
                                         c.id,
                                         c.author_id,
                                         c.title,
                                         c.description,
                                         c.cover_image_id,
                                         c.course_id,
                                         cr.title AS course_title,
                                         cr.slug AS course_slug,
                                         c.status,
                                         c.access_type,
                                         c.created_at,
                                         c.updated_at
                                     FROM collections c
                                     LEFT JOIN courses cr ON cr.id = c.course_id
                                     WHERE c.id = @CollectionId
                                       AND (c.status = 'PUBLISHED' OR c.author_id = @UserId);
                                     """;

        CollectionRow? collection = await connection.QueryFirstOrDefaultAsync<CollectionRow>(
            new CommandDefinition(
                collectionSql,
                new { query.CollectionId, UserId = _userData.IsAuthenticated ? _userData.UserId : Guid.Empty },
                cancellationToken: cancellationToken));

        if (collection is null)
            return EducationErrors.CollectionNotFound(query.CollectionId);

        // Двухуровневая семантика доступа:
        //  - header.IsAccessible — может ли юзер «полноценно» открыть подборку (author/admin/PUBLIC/
        //    REGISTERED+auth/enrollment-overlap).
        //  - per-item — даже если header заблокирован, итемы внутри могут быть PUBLIC и видны.
        //    Detail-эндпоинт всегда отдаёт структуру для PUBLISHED-подборки; markdown body не
        //    утекает (preview = NULL ниже), а UI рисует замок per-item.
        //  - DRAFT отдаётся ТОЛЬКО автору (см. SQL above) — для остальных вернётся NotFound.
        bool isAuthor = _userData.IsAuthenticated && collection.AuthorId == _userData.UserId;
        bool isAdmin = _userData.IsAdmin;

        // 2. Compute effective grants once для всего пейлода (header + items).
        List<string> effectiveGrants = [GrantTags.PUBLIC];
        if (_userData.IsAuthenticated)
        {
            effectiveGrants.Add(GrantTags.AUTHENTICATED);

            EntitlementGrantSet userGrants =
                await _entitlementReader.GetUserGrantTagsAsync(_userData.UserId, cancellationToken);
            effectiveGrants.AddRange(userGrants.Tags);
        }

        var grants = new EntitlementGrantSet(
            effectiveGrants.Distinct(StringComparer.Ordinal).ToArray());

        // 3. Header lock-state — author/admin bypass; non-author lock resolves below
        // after course-author batch lookup (single round-trip with item courseIds).
        AccessLockResult headerLock = (isAuthor || isAdmin)
            ? new AccessLockResult(IsAccessible: true, LockReason: null)
            : new AccessLockResult(IsAccessible: false, LockReason: null);

        // 4. Fetch sections.
        const string sectionsSql = """
                                   SELECT id, title, description
                                   FROM collection_sections
                                   WHERE collection_id = @CollectionId
                                   ORDER BY sort_key;
                                   """;

        List<SectionRow> sections = (await connection.QueryAsync<SectionRow>(
            new CommandDefinition(
                sectionsSql,
                new { query.CollectionId },
                cancellationToken: cancellationToken))).ToList();

        // 5. Fetch items (generic после #491: MATERIAL + QUIZ) одним запросом с LEFT JOIN
        // по типу. preview намеренно NULL: коллекция — куратор со ссылками, превью (первые 280
        // символов markdown) принадлежит material-detail/feed-эндпоинтам и обязано
        // решаться per-material entitlement'ом. Иначе PUBLIC-подборка становится
        // каналом leak'а 280-char сниппетов для ENROLLED материалов другим курсом.
        // Фронт показывает title + kind + lock-иконку на уровне материала если нужно.
        List<Guid> sectionIds = sections.Select(s => s.Id).ToList();
        List<ItemRow> items = [];

        // Items SQL зеркалит SQL-предикат header'а (`status='PUBLISHED' OR author_id=@UserId`)
        // для ОБОИХ типов: авторам отдаём собственные DRAFT материалы/квизы для editor-preview,
        // не-авторам — только PUBLISHED. Иначе DRAFT-сущности внутри опубликованной подборки
        // светят титул и misleading lockReason='not_enrolled' анонимам.
        Dictionary<(string ItemType, Guid ReferenceId), CollectionItemAccessRow> itemAccessByRef = new();
        if (sectionIds.Count > 0)
        {
            const string itemsSql = """
                                    SELECT
                                        ci.id           AS item_id,
                                        ci.section_id,
                                        ci.item_type,
                                        ci.reference_id,
                                        q.title         AS quiz_title,
                                        CASE WHEN q.id IS NULL THEN NULL
                                             ELSE COALESCE(jsonb_array_length(q.questions), 0)
                                        END             AS quiz_questions_count,
                                        q.author_id     AS quiz_author_id,
                                        m.author_id     AS material_author_id,
                                        m.title         AS material_title,
                                        m.kind          AS material_kind,
                                        m.status        AS material_status,
                                        m.access_type   AS material_access_type,
                                        m.created_at    AS material_created_at,
                                        m.updated_at    AS material_updated_at,
                                        m.published_at  AS material_published_at,
                                        m.image_id      AS material_image_id,
                                        m.video_id      AS material_video_id,
                                        m.quiz_id       AS material_quiz_id
                                    FROM collection_items ci
                                    LEFT JOIN materials m ON m.id = ci.reference_id AND ci.item_type = 'MATERIAL'
                                    LEFT JOIN quizzes q   ON q.id = ci.reference_id AND ci.item_type = 'QUIZ'
                                    WHERE ci.section_id = ANY(@SectionIds)
                                      AND (
                                            (ci.item_type = 'MATERIAL' AND (m.status = 'PUBLISHED' OR m.author_id = @UserId))
                                         OR (ci.item_type = 'QUIZ'     AND (q.status = 'PUBLISHED' OR q.author_id = @UserId))
                                          )
                                    ORDER BY ci.sort_key;
                                    """;

            items = (await connection.QueryAsync<ItemRow>(
                new CommandDefinition(
                    itemsSql,
                    new
                    {
                        SectionIds = sectionIds.ToArray(),
                        UserId = _userData.IsAuthenticated ? _userData.UserId : Guid.Empty,
                    },
                    cancellationToken: cancellationToken))).ToList();

            // MATERIAL-строки → MaterialSummaryDto (preview = null, см. комментарий выше).
            // Ручная сборка вместо Dapper multimap: после LEFT JOIN сегмент материала у
            // QUIZ-строк целиком NULL, и ctor-matching positional record'а — зыбкая почва.
            foreach (ItemRow row in items.Where(i => i.IsMaterial))
            {
                row.Material = new MaterialSummaryDto(
                    row.ReferenceId,
                    row.MaterialAuthorId!.Value,
                    row.MaterialTitle!,
                    Preview: null,
                    row.MaterialKind!,
                    row.MaterialStatus!,
                    row.MaterialAccessType!,
                    row.MaterialCreatedAt!.Value,
                    row.MaterialUpdatedAt!.Value,
                    row.MaterialPublishedAt,
                    row.MaterialImageId,
                    row.MaterialVideoId,
                    row.MaterialQuizId);
            }

            // 6. Enrich material thumbnails (batch FileService для image+video) — нужно
            //    UI'у чтобы показать кадр видео / обложку статьи в timeline-итемах.
            //    Одна копия mutate'а: в каждом ItemRow подменяем Material на enriched-версию.
            //    QUIZ-items не участвуют — у них нет media-полей.
            List<MaterialSummaryDto> enrichedMaterials = await MaterialFeedEnricher
                .EnrichSummaryThumbnailsAsync(
                    items.Where(i => i.Material is not null).Select(i => i.Material!).ToList(),
                    _fileServiceClient,
                    _progressServiceClient,
                    cancellationToken);

            Dictionary<Guid, MaterialSummaryDto> enrichedByMaterialId =
                enrichedMaterials.ToDictionary(m => m.Id);
            foreach (ItemRow row in items)
            {
                if (row.Material is not null
                    && enrichedByMaterialId.TryGetValue(row.Material.Id, out MaterialSummaryDto? enriched))
                {
                    row.Material = enriched;
                }
            }

            // 7. Per-item access — точный lockReason для рендера замка на материале/квизе.
            //    Используем тот же CollectionItemAccessLoader, что list-эндпоинты, чтобы
            //    решение совпадало с card-level enrichment'ом. Loader сам фильтрует
            //    PUBLISHED-сущности, так что DRAFT'ы автора в map не попадают —
            //    автор-bypass в ResolveItemLock покрывает их отдельно.
            IReadOnlyDictionary<Guid, IReadOnlyList<CollectionItemAccessRow>> itemAccess =
                await CollectionItemAccessLoader.LoadAsync(
                    connection, [collection.Id], cancellationToken);
            itemAccessByRef = itemAccess
                .GetValueOrDefault(collection.Id, [])
                .ToDictionary(r => (r.ItemType, r.ReferenceId));
        }

        // Resolve header lock (skip for author/admin).
        if (!(isAuthor || isAdmin))
        {
            Guid[] headerCourseIds = collection.CourseId is null ? [] : [collection.CourseId.Value];
            IReadOnlyList<string> headerTags = ContentAccessTagBuilder.Build(
                collection.AccessType, collection.Id, headerCourseIds, _logger);

            headerLock = LockReasonResolver.Resolve(headerTags, grants, _userData.IsAuthenticated);
        }

        // 8. Resolve cover image URL.
        string? coverImageUrl = null;
        if (collection.CoverImageId is not null)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(collection.CoverImageId.Value, cancellationToken);

            if (fileResult is { IsSuccess: true, Value: not null })
                coverImageUrl = fileResult.Value.ContentUrl;
            else
                _logger.LogWarning(
                    "Failed to fetch cover image {ImageId} for collection {CollectionId}",
                    collection.CoverImageId, query.CollectionId);
        }

        // 9. Build tree with per-item lock-state.
        Dictionary<Guid, IReadOnlyList<CollectionItemDto>> itemsBySection = items
            .GroupBy(i => i.SectionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<CollectionItemDto>)g
                    .Select(i =>
                    {
                        AccessLockResult itemLock = ResolveItemLock(
                            i, itemAccessByRef, isAdmin, grants);
                        return new CollectionItemDto(
                            i.ItemId,
                            i.ReferenceId,
                            i.ItemType,
                            i.Material,
                            i.QuizTitle,
                            i.QuizQuestionsCount,
                            itemLock.IsAccessible,
                            itemLock.LockReason);
                    })
                    .ToList());

        IReadOnlyList<CollectionSectionDto> sectionDtos = sections
            .Select(s => new CollectionSectionDto(
                s.Id,
                s.Title,
                s.Description,
                itemsBySection.GetValueOrDefault(s.Id, [])))
            .ToList();

        return new CollectionDetailDto(
            collection.Id,
            collection.AuthorId,
            collection.Title,
            collection.Description,
            collection.CoverImageId,
            coverImageUrl,
            collection.CourseId,
            collection.CourseTitle,
            collection.CourseSlug,
            collection.Status,
            collection.AccessType,
            headerLock.IsAccessible,
            headerLock.LockReason,
            collection.CreatedAt,
            collection.UpdatedAt,
            sectionDtos);
    }

    private AccessLockResult ResolveItemLock(
        ItemRow item,
        IReadOnlyDictionary<(string ItemType, Guid ReferenceId), CollectionItemAccessRow> itemAccessByRef,
        bool isAdmin,
        EntitlementGrantSet grants)
    {
        // Admin bypass — тот же контракт, что в material detail.
        // Внимание: collection.AuthorId === user намеренно НЕ даёт доступа к чужим материалам
        // внутри подборки. Иначе автор сборки A с материалом автора B (ENROLLED) получил бы
        // isAccessible=true в DTO, хотя material-detail вернёт ему 403. Material-detail —
        // источник правды по содержимому, collection-detail — лишь куратор-обёртка.
        if (isAdmin)
            return new AccessLockResult(IsAccessible: true, LockReason: null);

        // Автору ИМЕННО ЭТОЙ сущности (материала/квиза) не блокируем (это покрывает и кейс
        // «collection author = item author», и cross-author подборку с собственным item'ом внутри).
        Guid? itemAuthorId = item.IsMaterial ? item.MaterialAuthorId : item.QuizAuthorId;
        if (_userData.IsAuthenticated && itemAuthorId == _userData.UserId)
            return new AccessLockResult(IsAccessible: true, LockReason: null);

        // Loader фильтрует только PUBLISHED — для DRAFT-сущности собственного автора
        // выше уже сработал бы short-circuit, для не-автора DRAFT даже не попал бы
        // в items SQL (он его отфильтровал по status). Сюда падают только PUBLISHED
        // материалы/квизы, не доступные текущему пользователю.
        if (!itemAccessByRef.TryGetValue((item.ItemType, item.ReferenceId), out CollectionItemAccessRow row))
        {
            return new AccessLockResult(IsAccessible: false, LockReason: LockReasons.NOT_ENROLLED);
        }

        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            row.AccessType, row.ReferenceId, row.CourseIds);

        return LockReasonResolver.Resolve(tags, grants, _userData.IsAuthenticated);
    }

    private sealed class CollectionRow
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
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class SectionRow
    {
        public Guid Id { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
    }

    /// <summary>
    ///     Плоская строка generic-item'а (#491). MATERIAL-строки несут material_*-поля
    ///     (из них руками собирается <see cref="MaterialSummaryDto"/>), QUIZ-строки — quiz_*.
    /// </summary>
    private sealed class ItemRow
    {
        public Guid ItemId { get; init; }
        public Guid SectionId { get; init; }
        public string ItemType { get; init; } = null!;
        public Guid ReferenceId { get; init; }

        public string? QuizTitle { get; init; }
        public int? QuizQuestionsCount { get; init; }
        public Guid? QuizAuthorId { get; init; }

        public Guid? MaterialAuthorId { get; init; }
        public string? MaterialTitle { get; init; }
        public string? MaterialKind { get; init; }
        public string? MaterialStatus { get; init; }
        public string? MaterialAccessType { get; init; }
        public DateTime? MaterialCreatedAt { get; init; }
        public DateTime? MaterialUpdatedAt { get; init; }
        public DateTime? MaterialPublishedAt { get; init; }
        public Guid? MaterialImageId { get; init; }
        public Guid? MaterialVideoId { get; init; }
        public Guid? MaterialQuizId { get; init; }

        public MaterialSummaryDto? Material { get; set; }

        public bool IsMaterial => string.Equals(
            ItemType, nameof(Domain.Collections.CollectionItemType.MATERIAL), StringComparison.Ordinal);
    }
}
