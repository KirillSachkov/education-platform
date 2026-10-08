using CommentService.Contracts.Comments.Requests;
using CommentService.Core.Database;
using CommentService.Domain;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Ownership;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Comments.Events;

namespace CommentService.Core.Features.Comments.UseCases;

public sealed class CreateCommentEndpoint : IEndpoint
{
    /// <summary>
    ///     Rate-limit policy applied to <c>POST /comments</c>. Registered in
    ///     <c>CommentService.Web.Configuration.RateLimiting</c>.
    /// </summary>
    public const string CREATE_COMMENT_RATE_LIMIT_POLICY = "create-comment";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/comments", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateCommentRequest request,
                    [FromServices] CreateCommentHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new CreateCommentCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Comments.WRITE)
            .RequireRateLimiting(CREATE_COMMENT_RATE_LIMIT_POLICY);
    }
}

public sealed class CreateCommentValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentValidator()
    {
        RuleFor(x => x.Request.EntityReference)
            .MustBeValueObject(entityReference => CommentEntityReference.Of(entityReference.Type, entityReference.Id));
        RuleFor(x => x.Request.Content).MustBeValueObject(Content.Of);
    }
}

public sealed record CreateCommentCommand(CreateCommentRequest Request) : ICommand;

public sealed class CreateCommentHandler : ICommandHandler<Guid, CreateCommentCommand>
{
    private const int PREVIEW_MAX_LENGTH = 280;

    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<CreateCommentCommand> _validator;
    private readonly ICommentsRepository _commentsRepository;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IOutboxService _outbox;
    private readonly ILogger<CreateCommentHandler> _logger;
    private readonly UserScopedData _user;

    public CreateCommentHandler(
        ILogger<CreateCommentHandler> logger,
        ICommentsRepository commentsRepository,
        IValidator<CreateCommentCommand> validator,
        ITransactionManager transactionManager,
        IEntitlementChecker entitlementChecker,
        IEducationContentServiceClient ecsClient,
        IOutboxService outbox,
        UserScopedData user)
    {
        _logger = logger;
        _commentsRepository = commentsRepository;
        _validator = validator;
        _transactionManager = transactionManager;
        _entitlementChecker = entitlementChecker;
        _ecsClient = ecsClient;
        _outbox = outbox;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(CreateCommentCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        string entityType = ResourceTypes.FromEntityType(command.Request.EntityReference.Type);
        Guid entityId = command.Request.EntityReference.Id;

        // ENTITLEMENT reason means SINTER matched a course tag — user is already enrolled.
        // PUBLIC/AUTHENTICATED_ONLY means the entity itself is open, but interaction requires enrollment.
        // Также используем результат для recovery/read-model поля target_author_id.
        // Author-feed НЕ доверяет этому snapshot для авторизации: он сверяет current
        // education bindings в том же PostgreSQL snapshot, что и comments query.
        Guid? targetAuthorId = null;
        EntityOwnershipDto? ownership = null;
        bool ownershipLookupFailed = false;
        try
        {
            Result<EntityOwnershipDto, Error> result =
                await _ecsClient.GetEntityOwnershipAsync(entityType, entityId, cancellationToken);
            if (result.IsSuccess)
            {
                ownership = result.Value;
                targetAuthorId = result.Value.AuthorId;
            }
            else
            {
                // Lookup пришёл с ошибкой (не exception) — комментарий создастся, а
                // recovery/read-model поле останется NULL до backfill. Это не ослабляет
                // author-feed IDOR boundary: feed авторизуется по current bindings.
                string errorCode = result.Error?.Messages is { Count: > 0 } messages
                    ? messages[0].Code
                    : "unknown";
                _logger.LogWarning(
                    "ECS ownership lookup returned failure for {EntityType}/{EntityId}: {ErrorCode}. "
                        + "Comment target_author_id will remain NULL until backfill.",
                    entityType,
                    entityId,
                    errorCode);
                ownershipLookupFailed = true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Could not resolve ownership for {EntityType}/{EntityId} — proceeding without target_author_id",
                entityType,
                entityId);
            ownershipLookupFailed = true;
        }

        bool isTargetManager = ownership is not null
            && (ownership.AuthorId == _user.UserId
                || ownership.CreatedByUserId == _user.UserId
                || ownership.ManagerUserIds?.Contains(_user.UserId) == true);

        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            _user.ToAccessSubject(), entityType, entityId, cancellationToken);

        if (!decision.IsGranted && !isTargetManager)
        {
            return Error.Authorization("access.denied", "Нет доступа к указанной сущности");
        }

        if (!isTargetManager
            && decision.Reason is (AccessReason.PUBLIC or AccessReason.AUTHENTICATED_ONLY))
        {
            if (ownership is null || ownershipLookupFailed)
            {
                _logger.LogWarning(
                    "Could not verify ownership for {EntityType}/{EntityId} — failing closed on enrollment check",
                    entityType,
                    entityId);
                return Error.Authorization("enrollment.required", "Необходима запись на курс");
            }

            if (ownership.CourseId.HasValue)
            {
                AccessDecision courseDecision = await _entitlementChecker.CheckAccessAsync(
                    _user.ToAccessSubject(), ResourceTypes.COURSE, ownership.CourseId.Value, cancellationToken);
                if (!courseDecision.IsGranted
                    || courseDecision.Reason is not (AccessReason.ENTITLEMENT or AccessReason.ADMIN_OR_AUTHOR))
                    return Error.Authorization("enrollment.required", "Необходима запись на курс");
            }
        }

        CommentId commentId = CommentId.Create();
        CommentEntityReference entityReference =
            CommentEntityReference.Of(command.Request.EntityReference.Type, command.Request.EntityReference.Id).Value;
        Content content = Content.Of(command.Request.Content).Value;
        Guid authorId = _user.UserId;

        // Копим данные для integration event'а, чтобы опубликовать после SaveChanges.
        Guid? parentId = null;
        Guid? parentAuthorId = null;
        Comment newComment;

        if (command.Request.ParentId == null)
        {
            newComment = Comment.CreateParent(
                authorId, entityReference, content, commentId, targetAuthorId);
            await _commentsRepository.AddAsync(newComment, cancellationToken);
        }
        else
        {
            CommentId parentCommentId = CommentId.Of(command.Request.ParentId.Value);

            Result<Comment, Error> getParentCommentResult =
                await _commentsRepository.GetBy(x => x.Id == parentCommentId && !x.IsDeleted, cancellationToken);

            if (getParentCommentResult.IsFailure)
            {
                return getParentCommentResult.Error;
            }

            Comment parentComment = getParentCommentResult.Value;

            if (parentComment.EntityReference.Type != entityReference.Type
                || parentComment.EntityReference.Id != entityReference.Id)
            {
                return Error.Validation(
                    "comment.parent.target.mismatch",
                    "Родительский комментарий относится к другой сущности");
            }

            parentId = parentComment.Id.Value;
            parentAuthorId = parentComment.AuthorId;

            // Current authoritative ownership wins over the denormalized parent snapshot.
            // The fallback is only for a temporary ECS lookup failure; ownership-change
            // events reconcile every row in the thread independently.
            Guid? inheritedTargetAuthorId = targetAuthorId ?? parentComment.TargetAuthorId;

            newComment = Comment.CreateChild(
                parentComment, authorId, content, commentId, inheritedTargetAuthorId);

            await _commentsRepository.AddAsync(newComment, cancellationToken);
        }

        // Publish integration event через durable outbox — атомарно commit'нется с domain-state.
        // NotificationService'ский handler резолвит двух получателей (reply-author + entity-owner)
        // и диспатчит CommentReplied / CommentOnOwnContent нотификации.
        string preview = content.Value.Length <= PREVIEW_MAX_LENGTH
            ? content.Value
            : content.Value[..PREVIEW_MAX_LENGTH];

        // Use the entity's own CreatedAt to keep the event timestamp aligned with the row
        // — calling DateTimeOffset.UtcNow a second time can drift by microseconds and confuse
        // consumers that order/dedupe by timestamp.
        await _outbox.PublishAsync(new CommentCreated(
            CommentId: commentId.Value,
            AuthorId: authorId,
            EntityType: command.Request.EntityReference.Type.ToString().ToLowerInvariant(),
            EntityId: command.Request.EntityReference.Id,
            ParentId: parentId,
            ParentAuthorId: parentAuthorId,
            Preview: preview,
            CreatedAt: new DateTimeOffset(newComment.CreatedAt, TimeSpan.Zero)));

        UnitResult<Error> commitedResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (commitedResult.IsFailure)
        {
            return commitedResult.Error;
        }

        _logger.LogInformation("Comment by id {CommentId} has been added.", commentId.Value);

        return commentId.Value;
    }
}
