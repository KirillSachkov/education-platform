using System.Data.Common;
using AuthService.Contracts.HttpCommunication;
using CommentService.Contracts;
using CommentService.Contracts.Comments;
using CommentService.Contracts.Comments.Dtos;
using CommentService.Contracts.Comments.Requests;
using CommentService.Domain;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace CommentService.Core.Features.Comments.UseCases;

public sealed class GetRootsCommentsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/comments", async Task<EndpointResult<CursorResponse<CommentDto>>> (
                    [AsParameters] GetRootsCommentsRequest request,
                    [FromServices] GetRootsCommentsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetRootsCommentsQuery(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.VIEW);
    }
}

public sealed record GetRootsCommentsQuery(GetRootsCommentsRequest Request) : IQuery;

public sealed class GetRootsCommentsQueryValidator : AbstractValidator<GetRootsCommentsQuery>
{
    public GetRootsCommentsQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MAX_PAGE_SIZE)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetRootsCommentsRequest.Limit)));
        RuleFor(x => x.Request)
            .MustBeValueObject(request => CommentEntityReference.Of(request.TargetType, request.TargetId));
    }
}

public sealed class GetRootsCommentsHandler : IQueryHandlerWithResult<CursorResponse<CommentDto>, GetRootsCommentsQuery>
{
    /// <summary>
    /// Сколько первых ответов на root-комментарий мы тянем сразу — для авто-раскрытия
    /// 1-го уровня в UI без N+1 запросов. Дальше юзер дозагружает курсором через
    /// <c>GET /comments/{parentId}</c>.
    /// </summary>
    private const int PREVIEW_CHILDREN_COUNT = 3;

    private readonly ITransactionManager _transactionManager;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IValidator<GetRootsCommentsQuery> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<GetRootsCommentsHandler> _logger;

    public GetRootsCommentsHandler(
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        IAuthServiceClient authServiceClient,
        IValidator<GetRootsCommentsQuery> validator,
        UserScopedData user,
        ILogger<GetRootsCommentsHandler> logger)
    {
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _authServiceClient = authServiceClient;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CursorResponse<CommentDto>, Error>> Handle(
        GetRootsCommentsQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        string targetType = query.Request.TargetType.Trim().ToLowerInvariant();

        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            targetType,
            query.Request.TargetId,
            cancellationToken);

        if (!decision.IsGranted)
        {
            _logger.LogWarning(
                "Access denied for user {UserId} to comments on {TargetType}/{TargetId}",
                _user.UserId,
                query.Request.TargetType,
                query.Request.TargetId);
            return Error.Authorization("access.denied", "Нет доступа к указанной сущности");
        }

        Cursor? cursor = Cursor.Decode(query.Request.Cursor);

        // Один round-trip:
        //   1. CTE roots — page по cursor'у (Limit + 1 для hasMore-флага).
        //   2. LATERAL cc — children_count (excluding soft-deleted) per root.
        //   3. LATERAL pc — first PreviewSize replies per root + per-preview has_more_children.
        //   4. Trailing query — total roots count для UI-счётчика.
        // Получаем flat row-set (root × preview), группируем в C# по root_id.
        //
        // Note: deleted root-комментарии возвращаются как placeholder'ы (content=NULL),
        // чтобы тред-структура была видна; soft-deleted children из preview скрываются полностью.
        const string dataSql = """
                               WITH roots AS (
                                   SELECT id, content, author_id, depth, path, created_at, updated_at, is_deleted,
                                          target_entity_type, target_entity_id
                                   FROM comments
                                   WHERE target_entity_type = @TargetType
                                     AND target_entity_id   = @TargetId
                                     AND depth = 0
                                     AND (
                                         @CursorCreatedAt IS NULL
                                         OR (created_at, id) > (@CursorCreatedAt, @CursorId)
                                     )
                                   ORDER BY created_at ASC, id ASC
                                   LIMIT @Limit
                               )
                               SELECT
                                   r.id                                                   AS root_id,
                                   CASE WHEN r.is_deleted THEN NULL ELSE r.content END    AS root_content,
                                   r.author_id                                            AS root_author_id,
                                   r.depth                                                AS root_depth,
                                   r.created_at                                           AS root_created_at,
                                   r.updated_at                                           AS root_updated_at,
                                   r.is_deleted                                           AS root_is_deleted,
                                   cc.children_count                                      AS root_children_count,
                                   pc.id                                                  AS preview_id,
                                   CASE WHEN pc.is_deleted THEN NULL ELSE pc.content END  AS preview_content,
                                   pc.author_id                                           AS preview_author_id,
                                   pc.depth                                               AS preview_depth,
                                   pc.created_at                                          AS preview_created_at,
                                   pc.updated_at                                          AS preview_updated_at,
                                   pc.is_deleted                                          AS preview_is_deleted,
                                   pc.has_more_children                                   AS preview_has_more_children
                               FROM roots r
                               LEFT JOIN LATERAL (
                                   SELECT COUNT(*)::int AS children_count
                                   FROM comments x
                                   WHERE x.path <@ r.path
                                     AND x.depth = r.depth + 1
                                     AND x.target_entity_type = r.target_entity_type
                                     AND x.target_entity_id   = r.target_entity_id
                                     AND x.is_deleted = false
                               ) cc ON TRUE
                               LEFT JOIN LATERAL (
                                   SELECT
                                       p.id, p.content, p.author_id, p.depth, p.path,
                                       p.created_at, p.updated_at, p.is_deleted,
                                       p.target_entity_type, p.target_entity_id,
                                       EXISTS (
                                           SELECT 1
                                           FROM comments y
                                           WHERE y.path <@ p.path
                                             AND y.depth = p.depth + 1
                                             AND y.target_entity_type = p.target_entity_type
                                             AND y.target_entity_id   = p.target_entity_id
                                             AND y.is_deleted = false
                                       ) AS has_more_children
                                   FROM comments p
                                   WHERE p.path <@ r.path
                                     AND p.depth = r.depth + 1
                                     AND p.target_entity_type = r.target_entity_type
                                     AND p.target_entity_id   = r.target_entity_id
                                     AND p.is_deleted = false
                                   ORDER BY p.created_at ASC, p.id ASC
                                   LIMIT @PreviewSize
                               ) pc ON TRUE
                               ORDER BY r.created_at ASC, r.id ASC, pc.created_at ASC, pc.id ASC;

                               SELECT COUNT(*)
                               FROM comments
                               WHERE target_entity_type = @TargetType
                                 AND target_entity_id   = @TargetId
                                 AND depth = 0
                                 AND is_deleted = false;
                               """;

        DbConnection connection = _transactionManager.GetDbConnection();

        // PreviewSize+1: лишний row играет роль маркера hasMore.
        // Курсор строится от последнего видимого preview; sentinel в DTO не попадает.
        using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                dataSql,
                new
                {
                    TargetType = targetType,
                    query.Request.TargetId,
                    CursorCreatedAt = cursor?.CreatedAt,
                    CursorId = cursor?.LastId,
                    Limit = query.Request.Limit + 1,
                    PreviewSize = PREVIEW_CHILDREN_COUNT + 1,
                },
                cancellationToken: cancellationToken));

        IReadOnlyList<RootChildRow> rows = (await multi.ReadAsync<RootChildRow>()).AsList();
        long totalCount = await multi.ReadSingleAsync<long>();

        // Группируем flat result в (root, previews[]). Порядок rows гарантирует, что
        // одинаковые root_id идут подряд — простой single-pass без Dictionary.
        var roots = new List<CommentDto>();
        var previewMap = new Dictionary<Guid, List<CommentDto>>();

        foreach (RootChildRow row in rows)
        {
            if (roots.Count == 0 || roots[^1].Id != row.RootId)
            {
                roots.Add(new CommentDto
                {
                    Id = row.RootId,
                    AuthorId = row.RootAuthorId,
                    Content = row.RootContent ?? string.Empty,
                    Depth = row.RootDepth,
                    CreatedAt = row.RootCreatedAt,
                    UpdatedAt = row.RootUpdatedAt,
                    IsDeleted = row.RootIsDeleted,
                    ChildrenCount = row.RootChildrenCount,
                    HasMoreChildren = row.RootChildrenCount > 0,
                    PreviewChildren = [],
                });
                previewMap[row.RootId] = new List<CommentDto>(PREVIEW_CHILDREN_COUNT + 1);
            }

            if (row.PreviewId.HasValue)
            {
                previewMap[row.RootId].Add(new CommentDto
                {
                    Id = row.PreviewId.Value,
                    AuthorId = row.PreviewAuthorId!.Value,
                    Content = row.PreviewContent ?? string.Empty,
                    Depth = row.PreviewDepth!.Value,
                    ParentId = row.RootId,
                    CreatedAt = row.PreviewCreatedAt!.Value,
                    UpdatedAt = row.PreviewUpdatedAt!.Value,
                    IsDeleted = row.PreviewIsDeleted!.Value,
                    HasMoreChildren = row.PreviewHasMoreChildren ?? false,
                });
            }
        }

        bool hasMore = roots.Count > query.Request.Limit;
        if (hasMore)
        {
            CommentDto removed = roots[^1];
            previewMap.Remove(removed.Id);
            roots.RemoveAt(roots.Count - 1);
        }

        // Один enrichment-batch на все коллекции — мутируем roots и previewMap.Values
        // прямо in-place. Плоский список здесь некорректен: CommentDto — record, и
        // `with {...}` подменяет только элемент в перебираемом списке, оставляя исходные
        // коллекции (которые попадают в response) с null-AuthorName.
        List<List<CommentDto>> commentLists = new(1 + previewMap.Count) { roots };
        commentLists.AddRange(previewMap.Values);

        await CommentAuthorEnricher.EnrichManyWithAuthorInfoAsync(
            commentLists,
            _authServiceClient,
            cancellationToken);

        // After enrichment values are mutable through `with` — пересоздаём root'ы с
        // attached PreviewChildren (отрезанным до PreviewSize) и preview-cursor'ом.
        for (int i = 0; i < roots.Count; i++)
        {
            List<CommentDto> previews = previewMap[roots[i].Id];

            string? previewNextCursor = null;
            if (previews.Count > PREVIEW_CHILDREN_COUNT)
            {
                CommentDto last = previews[PREVIEW_CHILDREN_COUNT - 1];
                previewNextCursor = Cursor.Encode(
                    DateTime.SpecifyKind(last.CreatedAt, DateTimeKind.Utc),
                    last.Id);
                previews.RemoveAt(previews.Count - 1);
            }

            roots[i] = roots[i] with
            {
                PreviewChildren = previews,
                PreviewChildrenNextCursor = previewNextCursor,
            };
        }

        string? nextCursor = hasMore
            ? Cursor.Encode(
                DateTime.SpecifyKind(roots[^1].CreatedAt, DateTimeKind.Utc),
                roots[^1].Id)
            : null;

        return new CursorResponse<CommentDto> { Items = roots, NextCursor = nextCursor, TotalCount = totalCount };
    }

    /// <summary>Flat result row из CTE+LATERAL запроса: одна строка на пару (root, preview-child).</summary>
    private sealed class RootChildRow
    {
        public Guid RootId { get; init; }
        public string? RootContent { get; init; }
        public Guid RootAuthorId { get; init; }
        public int RootDepth { get; init; }
        public DateTime RootCreatedAt { get; init; }
        public DateTime RootUpdatedAt { get; init; }
        public bool RootIsDeleted { get; init; }
        public int RootChildrenCount { get; init; }

        public Guid? PreviewId { get; init; }
        public string? PreviewContent { get; init; }
        public Guid? PreviewAuthorId { get; init; }
        public int? PreviewDepth { get; init; }
        public DateTime? PreviewCreatedAt { get; init; }
        public DateTime? PreviewUpdatedAt { get; init; }
        public bool? PreviewIsDeleted { get; init; }
        public bool? PreviewHasMoreChildren { get; init; }
    }
}
