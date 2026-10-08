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

public sealed class GetChildrenCommentsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/comments/{parentId:guid}", async Task<EndpointResult<CursorResponse<CommentDto>>> (
                    [FromRoute] Guid parentId,
                    [AsParameters] GetChildrenCommentsRequest request,
                    [FromServices] GetChildrenCommentsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetChildrenCommentsQuery(parentId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.VIEW);
    }
}

public sealed record GetChildrenCommentsQuery(Guid ParentId, GetChildrenCommentsRequest Request) : IQuery;

public sealed class GetChildrenCommentsQueryValidator : AbstractValidator<GetChildrenCommentsQuery>
{
    public GetChildrenCommentsQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MAX_PAGE_SIZE)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetChildrenCommentsRequest.Limit)));
        RuleFor(x => x.Request)
            .MustBeValueObject(request => CommentEntityReference.Of(request.TargetType, request.TargetId));
    }
}

public sealed class GetChildrenCommentsHandler : IQueryHandlerWithResult<CursorResponse<CommentDto>, GetChildrenCommentsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IValidator<GetChildrenCommentsQuery> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<GetChildrenCommentsHandler> _logger;

    public GetChildrenCommentsHandler(
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        IAuthServiceClient authServiceClient,
        IValidator<GetChildrenCommentsQuery> validator,
        UserScopedData user,
        ILogger<GetChildrenCommentsHandler> logger)
    {
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _authServiceClient = authServiceClient;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CursorResponse<CommentDto>, Error>> Handle(
        GetChildrenCommentsQuery query,
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

        // Single round-trip: the data query embeds a correlated EXISTS for child-presence,
        // and the count query runs alongside via QueryMultiple.
        //
        // Parent MUST match caller-supplied TargetType/TargetId to prevent cross-entity IDOR
        // (entitlement check above is on @TargetType/@TargetId, so the parent's stored scope
        //  must be the same, otherwise an attacker could resolve children under a parent
        //  that belongs to a gated entity while the entitlement check was performed on an
        //  entity they do have access to).
        //
        // Note: unlike GetRoots, both the data and count queries filter by is_deleted = false.
        // Deleted children are fully hidden (not shown as placeholders), so the count excludes them.
        const string dataSql = """
                               SELECT
                                   c.id,
                                   CASE WHEN c.is_deleted THEN NULL ELSE c.content END AS content,
                                   c.author_id,
                                   c.depth,
                                   c.created_at,
                                   c.updated_at,
                                   c.is_deleted,
                                   EXISTS (
                                       SELECT 1
                                       FROM comments gc
                                       WHERE gc.target_entity_type = c.target_entity_type
                                         AND gc.target_entity_id   = c.target_entity_id
                                         AND gc.depth = c.depth + 1
                                         AND gc.path <@ c.path
                                         AND gc.is_deleted = false
                                   ) AS has_more_children
                               FROM comments c
                                        JOIN comments p
                                             ON p.id = @ParentId
                                            AND p.target_entity_type = @TargetType
                                            AND p.target_entity_id   = @TargetId
                               WHERE c.target_entity_type = p.target_entity_type
                                 AND c.target_entity_id   = p.target_entity_id
                                 AND c.depth  = p.depth + 1
                                 AND c.path  <@ p.path
                                 AND c.is_deleted = false
                                 AND (
                                     @CursorCreatedAt IS NULL
                                     OR (c.created_at, c.id) > (@CursorCreatedAt, @CursorId)
                                 )
                               ORDER BY c.created_at ASC, c.id ASC
                               LIMIT @Limit;

                               SELECT COUNT(*)
                               FROM comments c
                                        JOIN comments p
                                             ON p.id = @ParentId
                                            AND p.target_entity_type = @TargetType
                                            AND p.target_entity_id   = @TargetId
                               WHERE c.target_entity_type = p.target_entity_type
                                 AND c.target_entity_id   = p.target_entity_id
                                 AND c.depth  = p.depth + 1
                                 AND c.path  <@ p.path
                                 AND c.is_deleted = false;
                               """;

        DbConnection connection = _transactionManager.GetDbConnection();

        using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                dataSql,
                new
                {
                    query.ParentId,
                    CursorCreatedAt = cursor?.CreatedAt,
                    CursorId = cursor?.LastId,
                    Limit = query.Request.Limit + 1,
                    TargetType = targetType,
                    query.Request.TargetId
                },
                cancellationToken: cancellationToken));

        List<CommentDto> comments = (await multi.ReadAsync<CommentDto>()).ToList();
        long totalCount = await multi.ReadSingleAsync<long>();

        bool hasMore = comments.Count > query.Request.Limit;
        if (hasMore)
        {
            comments.RemoveAt(comments.Count - 1);
        }

        await CommentAuthorEnricher.EnrichWithAuthorInfoAsync(
            comments,
            _authServiceClient,
            cancellationToken);

        string? nextCursor = hasMore
            ? Cursor.Encode(
                DateTime.SpecifyKind(comments[^1].CreatedAt, DateTimeKind.Utc),
                comments[^1].Id)
            : null;

        return new CursorResponse<CommentDto> { Items = comments, NextCursor = nextCursor, TotalCount = totalCount };
    }
}
