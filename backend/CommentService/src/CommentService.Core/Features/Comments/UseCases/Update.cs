using System.Data.Common;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace CommentService.Core.Features.Comments.UseCases;

public sealed class UpdateCommentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/comments/{id:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid id,
                    [FromBody] UpdateCommentRequest request,
                    [FromServices] UpdateCommentHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateCommentCommand(id, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.WRITE);
    }
}

public sealed class UpdateCommentValidator : AbstractValidator<UpdateCommentCommand>
{
    public UpdateCommentValidator()
    {
        RuleFor(x => x.Request.Content).MustBeValueObject(Content.Of);
    }
}

public sealed record UpdateCommentCommand(Guid Id, UpdateCommentRequest Request) : ICommand;

public sealed class UpdateCommentHandler : ICommandHandler<Guid, UpdateCommentCommand>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UpdateCommentCommand> _validator;
    private readonly ICommentsRepository _commentsRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly ILogger<UpdateCommentHandler> _logger;
    private readonly UserScopedData _user;

    public UpdateCommentHandler(
        ILogger<UpdateCommentHandler> logger,
        ICommentsRepository commentsRepository,
        IValidator<UpdateCommentCommand> validator,
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        UserScopedData user)
    {
        _logger = logger;
        _commentsRepository = commentsRepository;
        _validator = validator;
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(UpdateCommentCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        // Entitlement check first — avoid leaking comment existence to unenrolled users.
        // Lightweight Dapper query fetches only the entity reference without loading the full aggregate.
        const string entityRefSql = """
                                    SELECT target_entity_type, target_entity_id
                                    FROM comments
                                    WHERE id = @Id AND is_deleted = false
                                    LIMIT 1;
                                    """;

        DbConnection connection = _transactionManager.GetDbConnection();

        var entityRef = await connection.QuerySingleOrDefaultAsync<EntityRefRow>(
            new CommandDefinition(
                entityRefSql,
                new { Id = command.Id },
                cancellationToken: cancellationToken));

        if (entityRef is null)
        {
            return GeneralErrors.NotFound();
        }

        string resourceType = entityRef.TargetEntityType;

        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(),
            resourceType,
            entityRef.TargetEntityId,
            cancellationToken);

        if (!decision.IsGranted)
        {
            return Error.Authorization("access.denied", "Нет доступа к указанной сущности");
        }

        CommentId commentId = CommentId.Of(command.Id);
        Content content = Content.Of(command.Request.Content).Value;
        Guid authorId = _user.UserId;

        Result<Comment, Error> getCommentResult =
            await _commentsRepository.GetBy(
                x => x.Id == commentId && !x.IsDeleted,
                cancellationToken);

        if (getCommentResult.IsFailure)
        {
            return getCommentResult.Error;
        }

        Comment comment = getCommentResult.Value;

        if (comment.AuthorId != authorId)
        {
            return Error.Authorization(
                "comment.update.not.owner",
                "Изменение доступно только автору комментария");
        }

        comment.Update(content);

        UnitResult<Error> commitedResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (commitedResult.IsFailure)
        {
            return commitedResult.Error;
        }

        _logger.LogInformation("Comment by id {CommentId} has been updated.", commentId.Value);

        return commentId.Value;
    }

    /// <summary>
    /// Lightweight row for entity reference lookup (Dapper).
    /// Column names use snake_case matching the DB schema.
    /// </summary>
    private sealed record EntityRefRow
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Local — mapped by Dapper
        public string TargetEntityType { get; init; } = null!;

        // ReSharper disable once UnusedAutoPropertyAccessor.Local — mapped by Dapper
        public Guid TargetEntityId { get; init; }
    }
}
