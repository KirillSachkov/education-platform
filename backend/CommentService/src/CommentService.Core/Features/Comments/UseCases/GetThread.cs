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

public sealed class GetThreadCommentsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/comments/{parentId:guid}/thread", async Task<EndpointResult<CursorResponse<CommentDto>>> (
                    [FromRoute] Guid parentId,
                    [AsParameters] GetThreadCommentsRequest request,
                    [FromServices] GetThreadCommentsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetThreadCommentsQuery(parentId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.VIEW);
    }
}

public sealed record GetThreadCommentsQuery(Guid ParentId, GetThreadCommentsRequest Request) : IQuery;

public sealed class GetThreadCommentsQueryValidator : AbstractValidator<GetThreadCommentsQuery>
{
    public GetThreadCommentsQueryValidator()
    {
        RuleFor(x => x.Request.Limit)
            .InclusiveBetween(Constants.MIN_PAGE_SIZE, Constants.MAX_PAGE_SIZE)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetThreadCommentsRequest.Limit)));
        RuleFor(x => x.Request)
            .MustBeValueObject(request => CommentEntityReference.Of(request.TargetType, request.TargetId));
    }
}

public sealed class GetThreadCommentsHandler : IQueryHandlerWithResult<CursorResponse<CommentDto>, GetThreadCommentsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IValidator<GetThreadCommentsQuery> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<GetThreadCommentsHandler> _logger;

    public GetThreadCommentsHandler(
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        IAuthServiceClient authServiceClient,
        IValidator<GetThreadCommentsQuery> validator,
        UserScopedData user,
        ILogger<GetThreadCommentsHandler> logger)
    {
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _authServiceClient = authServiceClient;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CursorResponse<CommentDto>, Error>> Handle(
        GetThreadCommentsQuery query,
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
                "Access denied for user {UserId} to comment thread on {TargetType}/{TargetId}",
                _user.UserId,
                query.Request.TargetType,
                query.Request.TargetId);
            return Error.Authorization("access.denied", "Нет доступа к указанной сущности");
        }

        Cursor? cursor = Cursor.Decode(query.Request.Cursor);

        // Inline `has_more_children` EXISTS — saves a 2nd DB round-trip vs the separate
        // childCheckSql query. Counts and existence checks exclude soft-deleted children to
        // match GetChildren.cs semantics; deleted rows are still returned in the page as
        // placeholders (content=NULL) since the thread view shows them inline.
        const string dataSql = """
                               SELECT
                                   c.id,
                                   c.author_id,
                                   CASE WHEN c.is_deleted THEN NULL ELSE c.content END AS content,
                                   c.depth,
                                   (subpath(c.path, nlevel(c.path) - 2, 1))::text::uuid AS parent_id,
                                   CASE WHEN parent_comment.is_deleted THEN NULL ELSE parent_comment.content END AS parent_preview,
                                   c.created_at,
                                   c.updated_at,
                                   c.is_deleted,
                                   EXISTS (
                                       SELECT 1
                                       FROM comments cc
                                       WHERE cc.path <@ c.path
                                         AND cc.id <> c.id
                                         AND cc.depth = c.depth + 1
                                         AND cc.is_deleted = false
                                         AND cc.target_entity_type = c.target_entity_type
                                         AND cc.target_entity_id   = c.target_entity_id
                                   ) AS has_more_children
                               FROM comments c
                                        JOIN comments p ON p.id = @ParentId
                                        LEFT JOIN comments parent_comment
                                                  ON parent_comment.id = (subpath(c.path, nlevel(c.path) - 2, 1))::text::uuid
                               WHERE p.target_entity_type = @TargetType
                                 AND p.target_entity_id   = @TargetId
                                 AND c.target_entity_type = p.target_entity_type
                                 AND c.target_entity_id   = p.target_entity_id
                                 AND c.path  <@ p.path
                                 AND c.id <> p.id
                                 AND c.depth > p.depth
                                 AND (
                                     @CursorCreatedAt IS NULL
                                     OR (c.created_at, c.id) > (@CursorCreatedAt, @CursorId)
                                 )
                               ORDER BY c.created_at ASC, c.id ASC
                               LIMIT @Limit;

                               SELECT COUNT(*)
                               FROM comments c
                                        JOIN comments p ON p.id = @ParentId
                               WHERE p.target_entity_type = @TargetType
                                 AND p.target_entity_id   = @TargetId
                                 AND c.target_entity_type = p.target_entity_type
                                 AND c.target_entity_id   = p.target_entity_id
                                 AND c.path  <@ p.path
                                 AND c.id <> p.id
                                 AND c.depth > p.depth
                                 AND c.is_deleted = false;
                               """;

        DbConnection connection = _transactionManager.GetDbConnection();

        var param = new
        {
            query.ParentId,
            TargetType = targetType,
            query.Request.TargetId,
            CursorCreatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = query.Request.Limit + 1,
        };

        using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(dataSql, param, cancellationToken: cancellationToken));

        List<CommentDto> comments = (await multi.ReadAsync<CommentDto>()).ToList();
        long totalCount = await multi.ReadSingleAsync<long>();

        bool hasMore = comments.Count > query.Request.Limit;
        if (hasMore)
        {
            comments.RemoveAt(comments.Count - 1);
        }

        // has_more_children is now inlined into dataSql via EXISTS; no second round-trip needed.

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
