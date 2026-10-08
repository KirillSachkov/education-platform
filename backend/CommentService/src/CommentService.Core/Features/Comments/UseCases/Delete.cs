using System.Data.Common;
using CommentService.Domain;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace CommentService.Core.Features.Comments.UseCases;

public sealed class DeleteCommentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/comments/{id:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid id,
                    [FromServices] DeleteCommentHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new DeleteCommentCommand(id), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.WRITE);
    }
}

public sealed class DeleteCommentValidator : AbstractValidator<DeleteCommentCommand>
{
    public DeleteCommentValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithError(GeneralErrors.ValueIsRequired("commentId"));
    }
}

public sealed record DeleteCommentCommand(Guid Id) : ICommand;

public sealed class DeleteCommentHandler : ICommandHandler<Guid, DeleteCommentCommand>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<DeleteCommentCommand> _validator;
    private readonly ICommentsRepository _commentsRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ILogger<DeleteCommentHandler> _logger;
    private readonly UserScopedData _user;

    public DeleteCommentHandler(
        ILogger<DeleteCommentHandler> logger,
        ICommentsRepository commentsRepository,
        IValidator<DeleteCommentCommand> validator,
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        UserScopedData user)
    {
        _logger = logger;
        _commentsRepository = commentsRepository;
        _validator = validator;
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(DeleteCommentCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        CommentId commentId = CommentId.Of(command.Id);

        // Entitlement check FIRST — uses a lightweight Dapper lookup (target_entity_type,
        // target_entity_id only) so we never load the full aggregate for callers who don't
        // have access. Mirrors the pattern in UpdateCommentHandler.
        // Without this guard, an unenrolled caller could enumerate comment GUIDs by
        // observing 404 vs 403 — the entity load reveals existence before the entitlement
        // check runs.
        const string entityRefSql = """
                                    SELECT target_entity_type, target_entity_id
                                    FROM comments
                                    WHERE id = @Id AND is_deleted = false
                                    LIMIT 1;
                                    """;

        DbConnection connection = _transactionManager.GetDbConnection();
        var entityRef = await connection.QuerySingleOrDefaultAsync<EntityRefRow>(
            new CommandDefinition(entityRefSql, new { Id = command.Id }, cancellationToken: cancellationToken));

        if (entityRef is null)
            return GeneralErrors.NotFound();

        string entityType = entityRef.TargetEntityType;

        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(), entityType, entityRef.TargetEntityId, cancellationToken);

        if (!decision.IsGranted)
            return Error.Authorization("access.denied", "Нет доступа к указанной сущности");

        // Entitled — now load the full aggregate for ownership/moderation checks + SoftDelete.
        Result<Comment, Error> getCommentResult =
            await _commentsRepository.GetBy(
                x => x.Id == commentId && !x.IsDeleted,
                cancellationToken);

        if (getCommentResult.IsFailure)
            return getCommentResult.Error;

        Comment comment = getCommentResult.Value;

        // Ownership / moderation check.
        // Platform moderators (anyone with Comments.MODERATE permission — currently Moderator + Admin)
        // bypass the ownership check so they can remove abusive comments on any entity.
        bool isCommentOwner = comment.AuthorId == _user.UserId;
        bool canModerate = _user.HasPermission(PlatformPermissions.Comments.MODERATE);

        if (!isCommentOwner && !canModerate)
        {
            var ownershipResult = await _ecsClient.GetEntityOwnershipAsync(
                entityType, comment.EntityReference.Id, cancellationToken);

            if (ownershipResult.IsFailure)
            {
                _logger.LogWarning(
                    "ECS GetEntityOwnership failed for {EntityType}:{EntityId} during comment {CommentId} delete; denying for safety",
                    entityType, comment.EntityReference.Id, commentId.Value);
                return Error.Authorization("access.denied", "Не удалось проверить владельца ресурса");
            }

            bool isCourseAuthor = ownershipResult.Value.AuthorId == _user.UserId;

            if (!isCourseAuthor)
                return Error.Authorization("access.denied", "Удаление доступно только автору комментария или владельцу контента");
        }

        comment.SoftDelete();

        UnitResult<Error> commitedResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (commitedResult.IsFailure)
        {
            return commitedResult.Error;
        }

        _logger.LogInformation(
            "Comment {CommentId} has been soft deleted.",
            commentId.Value);

        return commentId.Value;
    }

    private sealed record EntityRefRow
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Local — mapped by Dapper
        public string TargetEntityType { get; init; } = null!;

        // ReSharper disable once UnusedAutoPropertyAccessor.Local — mapped by Dapper
        public Guid TargetEntityId { get; init; }
    }
}
