using System.Data.Common;
using AuthUserLookupDto = AuthService.Contracts.AuthUserLookupDto;
using AuthService.Contracts.HttpCommunication;
using CommentService.Contracts;
using CommentService.Contracts.Comments;
using CommentService.Contracts.Comments.Dtos;
using CommentService.Contracts.Comments.Requests;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Materials;
using EducationContentService.Contracts.Ownership;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace CommentService.Core.Features.Comments.UseCases;

/// <summary>
/// `GET /comments/author-feed/` — лента всех комментариев под контентом текущего автора.
/// Используется страницей `/author/comments` (раздел «Преподавание») как YouTube Studio
/// Comments — единая лента вместо обхода материал-за-материалом.
///
/// Tier 1: <see cref="PlatformPermissions.Comments.VIEW"/>.
/// Tier 2: same-snapshot SQL join to canonical ECS ownership (admin bypass — видит всё).
/// Tier 3: автор владеет своим контентом, per-item entitlement-чек не нужен.
/// </summary>
public sealed class GetAuthorFeedEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/comments/author-feed/", async Task<EndpointResult<CursorResponse<AuthorFeedCommentDto>>> (
                    [AsParameters] GetAuthorFeedRequest request,
                    [FromServices] GetAuthorFeedHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetAuthorFeedQuery(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.VIEW);
    }
}

public sealed record GetAuthorFeedQuery(GetAuthorFeedRequest Request) : IQuery;

public sealed class GetAuthorFeedQueryValidator : AbstractValidator<GetAuthorFeedQuery>
{
    public GetAuthorFeedQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MAX_PAGE_SIZE)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetAuthorFeedRequest.Limit)));
    }
}

public sealed class GetAuthorFeedHandler
    : IQueryHandlerWithResult<CursorResponse<AuthorFeedCommentDto>, GetAuthorFeedQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IValidator<GetAuthorFeedQuery> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<GetAuthorFeedHandler> _logger;

    public GetAuthorFeedHandler(
        ITransactionManager transactionManager,
        IAuthServiceClient authServiceClient,
        IEducationContentServiceClient ecsClient,
        IValidator<GetAuthorFeedQuery> validator,
        UserScopedData user,
        ILogger<GetAuthorFeedHandler> logger)
    {
        _transactionManager = transactionManager;
        _authServiceClient = authServiceClient;
        _ecsClient = ecsClient;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CursorResponse<AuthorFeedCommentDto>, Error>> Handle(
        GetAuthorFeedQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // .RequirePermissions(Comments.VIEW) on the endpoint guarantees authenticated user
        // → UserId != Guid.Empty by the time we reach the handler.
        Cursor? cursor = Cursor.Decode(query.Request.Cursor);
        Guid userId = _user.UserId;
        bool isAdmin = _user.IsAdmin;
        GetAuthorFeedRequest request = query.Request;

        // Запрос всегда отдаёт DESC по (created_at, id) — самые свежие комменты сверху.
        // Cursor хранит границу предыдущей страницы; на следующую идём через
        // (c.created_at, c.id) < (@CursorCreatedAt, @CursorId).
        //
        // ВАЖНО: target_owners вычисляется из текущих education bindings в ЭТОМ ЖЕ
        // PostgreSQL snapshot. target_author_id остаётся recovery/read-model полем, но не
        // участвует в IDOR boundary: attach/detach/transfer сразу меняют авторизацию feed.
        //
        // hasMyReply считается через ltree-subtree: ищем существование любой реплики
        // автора в поддереве c.path (depth > c.depth исключает сам корневой коммент).
        //
        // Для unreadOnly берём viewed_at из author_feed_state (или -infinity если нет).
        //
        // ParentId/ParentPreview не выдаются на этом этапе — фронт показывает контекст
        // через target_entity_type/_id и title; reply chain не нужен в feed-режиме.
        // hasMyReply считается один раз через LATERAL — Postgres вычислит EXISTS
        // per-row для candidate'а и переиспользует значение в SELECT и WHERE,
        // не дублируя subquery.
        string sql = $"""
                           WITH viewed AS (
                               SELECT COALESCE(
                                   (SELECT viewed_at FROM author_feed_state WHERE author_id = @CallerUserId),
                                   '-infinity'::timestamptz
                               ) AS viewed_at
                           )
                           SELECT
                               c.id                                                AS Id,
                               CASE WHEN c.is_deleted THEN NULL ELSE c.content END AS Content,
                               c.author_id                                         AS AuthorId,
                               c.depth                                             AS Depth,
                               c.created_at                                        AS CreatedAt,
                               c.updated_at                                        AS UpdatedAt,
                               c.is_deleted                                        AS IsDeleted,
                               c.target_entity_type                                AS TargetEntityType,
                               c.target_entity_id                                  AS TargetEntityId,
                               NULL::uuid                                          AS ParentId,
                               NULL::text                                          AS ParentPreview,
                               reply.has_reply                                     AS HasMyReply,
                               c.created_at > (SELECT viewed_at FROM viewed)       AS IsUnread
                           FROM comments c
                           LEFT JOIN {EntityOwnershipDatabaseContract.COMMENT_TARGET_OWNERSHIP_VIEW_V1} owner
                             ON owner.target_entity_type = c.target_entity_type
                            AND owner.target_entity_id = c.target_entity_id
                           LEFT JOIN LATERAL (
                               SELECT EXISTS (
                                   SELECT 1
                                   FROM comments r
                                   WHERE r.path <@ c.path
                                     AND r.depth > c.depth
                                     AND r.target_entity_type = c.target_entity_type
                                     AND r.target_entity_id = c.target_entity_id
                                     AND r.author_id = @CallerUserId
                                     AND r.is_deleted = false
                               ) AS has_reply
                           ) reply ON TRUE
                           WHERE c.is_deleted = false
                             AND c.author_id <> @CallerUserId
                             AND (@IsAdmin = true OR owner.author_id = @CallerUserId)
                             AND (
                                 @CursorCreatedAt IS NULL
                                 OR (c.created_at, c.id) < (@CursorCreatedAt, @CursorId)
                             )
                             AND (@WithoutReply = false OR reply.has_reply = false)
                             AND (
                                 @UnreadOnly = false
                                 OR c.created_at > (SELECT viewed_at FROM viewed)
                             )
                           ORDER BY c.created_at DESC, c.id DESC
                           LIMIT @Limit;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        List<AuthorFeedRow> rows = (await connection.QueryAsync<AuthorFeedRow>(
                new CommandDefinition(
                    sql,
                    new
                    {
                        CallerUserId = userId,
                        IsAdmin = isAdmin,
                        WithoutReply = request.WithoutReply,
                        UnreadOnly = request.UnreadOnly,
                        CursorCreatedAt = cursor?.CreatedAt,
                        CursorId = cursor?.LastId,
                        Limit = request.Limit + 1
                    },
                    cancellationToken: cancellationToken)))
            .ToList();

        bool hasMore = rows.Count > request.Limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        List<AuthorFeedCommentDto> items = rows
            .Select(r => r.ToDto())
            .ToList();

        if (items.Count > 0)
        {
            await EnrichWithAuthorInfoAsync(items, cancellationToken);
            await EnrichWithTargetTitlesAsync(items, cancellationToken);
        }

        string? nextCursor = hasMore && items.Count > 0
            ? Cursor.Encode(
                DateTime.SpecifyKind(items[^1].CreatedAt, DateTimeKind.Utc),
                items[^1].Id)
            : null;

        return new CursorResponse<AuthorFeedCommentDto>
        {
            Items = items,
            NextCursor = nextCursor,
            // TotalCount = 0 — для feed'а со множеством разных фильтров total потребовал бы
            // повторного скана, а UI ленты не показывает «X из N». Поле required в контракте,
            // поэтому заглушка вместо nullable.
            TotalCount = 0
        };
    }

    private async Task EnrichWithAuthorInfoAsync(
        List<AuthorFeedCommentDto> items,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> authorIds = items
            .Select(c => c.AuthorId)
            .Distinct()
            .ToList();

        var result = await _authServiceClient.GetUsersByIdsAsync(authorIds, cancellationToken);
        if (result.IsFailure)
        {
            return;
        }

        Dictionary<Guid, AuthUserLookupDto> userById = result.Value.ToDictionary(u => u.UserId);

        for (int i = 0; i < items.Count; i++)
        {
            if (userById.TryGetValue(items[i].AuthorId, out AuthUserLookupDto? user))
            {
                items[i] = items[i] with
                {
                    AuthorName = user.Name,
                    AuthorUsername = user.Username,
                    AuthorAvatarId = user.AvatarId,
                };
            }
        }
    }

    /// <summary>
    /// Резолвит title целевых сущностей через ECS internal HTTP-эндпоинт
    /// `/internal/materials/titles`. Если ECS недоступен — items отдаются без
    /// TargetTitle (UI рендерит «Материал» без заголовка).
    /// </summary>
    private async Task EnrichWithTargetTitlesAsync(
        List<AuthorFeedCommentDto> items,
        CancellationToken cancellationToken)
    {
        Guid[] materialIds = items
            .Where(i => string.Equals(i.TargetEntityType, "material", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.TargetEntityId)
            .Distinct()
            .ToArray();

        if (materialIds.Length == 0)
        {
            return;
        }

        Result<IReadOnlyList<MaterialTitleDto>, Error> result =
            await _ecsClient.GetMaterialTitlesAsync(materialIds, cancellationToken);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to enrich author feed with target titles: {ErrorCode}",
                result.Error.Messages is { Count: > 0 } messages ? messages[0].Code : "unknown");
            return;
        }

        Dictionary<Guid, string> titleById = result.Value.ToDictionary(t => t.MaterialId, t => t.Title);

        for (int i = 0; i < items.Count; i++)
        {
            if (titleById.TryGetValue(items[i].TargetEntityId, out string? title))
            {
                items[i] = items[i] with { TargetTitle = title };
            }
        }
    }

    /// <summary>
    /// Промежуточная row-проекция Dapper'а — class с init-properties, чтобы Dapper мог
    /// материализовать row через parameterless ctor + property-set, без жёстких требований
    /// к match'у positional record-конструктора с типами колонок.
    /// </summary>
    private sealed class AuthorFeedRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string? Content { get; init; }
        public int Depth { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public bool IsDeleted { get; init; }
        public string TargetEntityType { get; init; } = string.Empty;
        public Guid TargetEntityId { get; init; }
        public Guid? ParentId { get; init; }
        public string? ParentPreview { get; init; }
        public bool HasMyReply { get; init; }
        public bool IsUnread { get; init; }

        public AuthorFeedCommentDto ToDto() => new()
        {
            Id = Id,
            AuthorId = AuthorId,
            Content = Content,
            Depth = Depth,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            IsDeleted = IsDeleted,
            TargetEntityType = TargetEntityType,
            TargetEntityId = TargetEntityId,
            ParentId = ParentId,
            ParentPreview = ParentPreview,
            HasMyReply = HasMyReply,
            IsUnread = IsUnread,
        };
    }
}
