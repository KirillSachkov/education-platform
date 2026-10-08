using System.Data.Common;
using CommentService.Contracts.Comments.Dtos;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace CommentService.Core.Features.Comments.UseCases;

/// <summary>
/// GET /comments/{commentId}/ancestors — цепочка предков для конкретного коммента.
/// Используется фронтом для auto-expand на deep-link <c>?focus=&lt;id&gt;</c>: чтобы
/// раскрыть всех ancestors одним проходом, не дёргая <c>/comments/{parentId}</c> N раз.
/// </summary>
public sealed class GetCommentAncestorsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/comments/{commentId:guid}/ancestors",
                async Task<EndpointResult<CommentAncestorsDto>> (
                    [FromRoute] Guid commentId,
                    [FromServices] GetCommentAncestorsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCommentAncestorsQuery(commentId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.VIEW);
    }
}

public sealed record GetCommentAncestorsQuery(Guid CommentId) : IQuery;

public sealed class GetCommentAncestorsHandler
    : IQueryHandlerWithResult<CommentAncestorsDto, GetCommentAncestorsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _user;
    private readonly ILogger<GetCommentAncestorsHandler> _logger;

    public GetCommentAncestorsHandler(
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        UserScopedData user,
        ILogger<GetCommentAncestorsHandler> logger)
    {
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CommentAncestorsDto, Error>> Handle(
        GetCommentAncestorsQuery query,
        CancellationToken cancellationToken = default)
    {
        // snake_case + Dapper.DefaultTypeMap.MatchNamesWithUnderscores маппит
        // target_entity_type → TargetEntityType и т.п. (см. Registration.cs).
        // path хранится как ltree — `::text` приводит к string для .NET-маппинга.
        const string sql = """
                           SELECT
                               c.target_entity_type,
                               c.target_entity_id,
                               c.depth,
                               c.path::text AS path
                           FROM comments c
                           WHERE c.id = @CommentId
                             AND c.is_deleted = FALSE
                           LIMIT 1;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();
        AncestorRow? row = await connection
            .QuerySingleOrDefaultAsync<AncestorRow>(new CommandDefinition(
                sql,
                new { query.CommentId },
                cancellationToken: cancellationToken));

        if (row is null)
        {
            return GeneralErrors.NotFound();
        }

        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            row.TargetEntityType,
            row.TargetEntityId,
            cancellationToken);

        if (!decision.IsGranted)
        {
            _logger.LogWarning(
                "Access denied for user {UserId} to comment {CommentId} ancestors on {TargetType}/{TargetId}",
                _user.UserId,
                query.CommentId,
                row.TargetEntityType,
                row.TargetEntityId);
            return Error.Authorization("access.denied", "Нет доступа к указанной сущности");
        }

        // Path формата "uuid-1.uuid-2.uuid-3". Последний сегмент — сам коммент,
        // всё до него — ancestors в порядке root → direct parent.
        string[] segments = row.Path.Split('.');
        Guid[] ancestorIds = segments.Length > 1
            ? segments
                .Take(segments.Length - 1)
                .Select(static s => Guid.Parse(s))
                .ToArray()
            : Array.Empty<Guid>();

        return new CommentAncestorsDto
        {
            TargetType = row.TargetEntityType,
            TargetId = row.TargetEntityId,
            Depth = row.Depth,
            AncestorIds = ancestorIds,
        };
    }

    /// <summary>
    /// Lightweight row для Dapper-маппинга (init-props, не positional record —
    /// positional ctor не маппится через MatchNamesWithUnderscores). Поля повторяют
    /// snake_case колонок из `comments` через convention.
    /// </summary>
    private sealed record AncestorRow
    {
        public string TargetEntityType { get; init; } = null!;
        public Guid TargetEntityId { get; init; }
        public int Depth { get; init; }
        public string Path { get; init; } = null!;
    }
}
