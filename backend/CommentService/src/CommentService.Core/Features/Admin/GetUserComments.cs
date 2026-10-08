using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace CommentService.Core.Features.Admin;

public sealed record GetUserCommentsForAdminQuery(Guid UserId, int Limit) : IQuery;

public sealed class GetUserCommentsForAdminQueryValidator
    : AbstractValidator<GetUserCommentsForAdminQuery>
{
    public GetUserCommentsForAdminQueryValidator()
    {
        RuleFor(query => query.Limit)
            .InclusiveBetween(1, 200)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetUserCommentsForAdminQuery.Limit)));
    }
}

public sealed record AdminUserCommentRow(
    Guid Id,
    string EntityType,
    Guid EntityId,
    Guid? ParentId,
    string BodyPreview,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt);

public sealed record AdminUserCommentsResponse(IReadOnlyList<AdminUserCommentRow> Items);

public sealed class GetUserCommentsForAdminEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/comments/admin/users/{userId:guid}/recent",
                async Task<EndpointResult<AdminUserCommentsResponse>> (
                    Guid userId,
                    [FromQuery] int? limit,
                    [FromServices] GetUserCommentsForAdminHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(
                    new GetUserCommentsForAdminQuery(userId, limit ?? 50),
                    ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

public sealed class GetUserCommentsForAdminHandler
    : IQueryHandlerWithResult<AdminUserCommentsResponse, GetUserCommentsForAdminQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetUserCommentsForAdminQuery> _validator;

    public GetUserCommentsForAdminHandler(
        ITransactionManager transactionManager,
        IValidator<GetUserCommentsForAdminQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<AdminUserCommentsResponse, Error>> Handle(
        GetUserCommentsForAdminQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // Колонки выровнены под фактическую схему (миграция 20260412192233_Init): author_id,
        // target_entity_type, target_entity_id, content, deletion_date. Раньше SQL ссылался на
        // несуществующие entity_type/entity_id/user_id/body/deleted_at/parent_id → каждый вызов
        // падал с Postgres-ошибкой 500. parent_id колонки нет (иерархия — ltree path), поэтому
        // ParentId отдаём NULL — для admin-списка «недавние комментарии юзера» это не нужно.
        const string sql = """
            SELECT
                id                  AS "Id",
                target_entity_type  AS "EntityType",
                target_entity_id    AS "EntityId",
                NULL::uuid          AS "ParentId",
                LEFT(content, 280)  AS "BodyPreview",
                created_at          AS "CreatedAt",
                updated_at          AS "UpdatedAt",
                deletion_date       AS "DeletedAt"
            FROM comments
            WHERE author_id = @UserId
            ORDER BY created_at DESC, id DESC
            LIMIT @Limit;
            """;

        IEnumerable<AdminUserCommentRow> rows =
            await connection.QueryAsync<AdminUserCommentRow>(new CommandDefinition(
                sql,
                new { UserId = query.UserId, query.Limit },
                cancellationToken: cancellationToken));

        return new AdminUserCommentsResponse(rows.ToList());
    }
}
