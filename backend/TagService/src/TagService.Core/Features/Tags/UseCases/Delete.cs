using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Tags.Events;
using TagService.Core.Database;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.UseCases;

public sealed class DeleteTagEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/tags/{id:guid}", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid id,
            [FromServices] DeleteTagHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new DeleteTagCommand(id), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class DeleteTagValidator : AbstractValidator<DeleteTagCommand>
{
    public DeleteTagValidator()
    {
        RuleFor(x => x.TagId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("tagId"));
    }
}

public sealed record DeleteTagCommand(Guid TagId) : ICommand;

public sealed class DeleteTagHandler : ICommandHandler<Guid, DeleteTagCommand>
{
    private readonly ITagsRepository _tagsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly UserScopedData _user;
    private readonly IValidator<DeleteTagCommand> _validator;
    private readonly ILogger<DeleteTagHandler> _logger;

    public DeleteTagHandler(
        ITagsRepository tagsRepository,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        UserScopedData user,
        IValidator<DeleteTagCommand> validator,
        ILogger<DeleteTagHandler> logger)
    {
        _tagsRepository = tagsRepository;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(DeleteTagCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        TagId tagId = TagId.Of(command.TagId);

        UnitResult<Error> beginTransactionResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (beginTransactionResult.IsFailure)
            return beginTransactionResult.Error;

        UnitResult<Error> lockResult = await _tagsRepository.AcquireMutationLocksAsync([tagId], cancellationToken);
        if (lockResult.IsFailure)
            return lockResult.Error;

        Result<Tag, Error> tagResult = await _tagsRepository.GetBy(t => t.Id == tagId, cancellationToken);

        // Idempotent semantics — deleting a non-existent tag is a no-op success.
        // This matches the DELETE /tags/{id} contract documented in CLAUDE.md and
        // keeps clients from needing a GET-then-DELETE dance for cleanup flows.
        if (tagResult.IsFailure)
            return tagId.Value;

        if (!_user.IsOwnerOrAdmin(tagResult.Value.AuthorId))
        {
            _logger.LogWarning(
                "Authorization denied: User {UserId} attempted to delete tag {TagId} owned by {OwnerId}.",
                _user.UserId,
                tagId.Value,
                tagResult.Value.AuthorId);
            return Error.Authorization("tag.not_owned", "Тег принадлежит другому автору");
        }

        UnitResult<Error> deleteTagResult = tagResult.Value.Kind == TagKind.CANON
            ? await _tagsRepository.DeleteCanonicalTagAndRestoreAliasesAsync(tagId, cancellationToken)
            : await _tagsRepository.DeleteTagAsync(tagId, cancellationToken);

        if (deleteTagResult.IsFailure)
            return deleteTagResult.Error;

        await _outboxService.PublishAsync(new TagsDeleted([tagId.Value]));

        UnitResult<Error> saveResult = await _transactionManager.CommitTransactionAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Tag {TagId} ({TagTitle}) has been deleted.",
            tagId.Value, tagResult.Value.Title.Value);

        return tagId.Value;
    }
}
