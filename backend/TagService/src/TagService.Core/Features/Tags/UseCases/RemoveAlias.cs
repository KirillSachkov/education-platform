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
using TagService.Contracts.Tags.Requests;
using TagService.Core.Database;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.UseCases;

public sealed class RemoveAliasEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/tags/{id:guid}/aliases", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid id,
            [FromBody] RemoveAliasRequest request,
            [FromServices] RemoveAliasHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new RemoveAliasCommand(id, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class RemoveAliasValidator : AbstractValidator<RemoveAliasCommand>
{
    public RemoveAliasValidator()
    {
        RuleFor(x => x.TagId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("tagId"));

        RuleFor(x => x.Request.TagIds)
            .Must(x => x.Count == x.Distinct().Count())
            .WithError(Error.Validation("tags.aliases.duplicates", "Идентификаторы тегов-алиасов должны быть уникальными"))
            .Must(x => x.Count > 0)
            .WithError(Error.Validation("tags.aliases.empty", "Необходимо указать хотя бы один идентификатор тега"))
            .Must(x => x.Count <= Constants.MAX_TAGS_PER_MUTATION)
            .WithError(Error.Validation(
                "tags.request.too_many",
                $"Допустимо не более {Constants.MAX_TAGS_PER_MUTATION} тегов за один запрос"));

        RuleFor(x => x)
            .Must(x => x.Request.TagIds.All(t => t != x.TagId))
            .WithError(Error.Validation("tags.merge.self", "Основной тег не должен быть среди алиасов"));
    }
}

public sealed record RemoveAliasCommand(Guid TagId, RemoveAliasRequest Request) : ICommand;

public sealed class RemoveAliasHandler : ICommandHandler<Guid, RemoveAliasCommand>
{
    private readonly ITagsRepository _tagsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly UserScopedData _user;
    private readonly IValidator<RemoveAliasCommand> _validator;
    private readonly ILogger<RemoveAliasHandler> _logger;

    public RemoveAliasHandler(
        ITagsRepository tagsRepository,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        UserScopedData user,
        IValidator<RemoveAliasCommand> validator,
        ILogger<RemoveAliasHandler> logger)
    {
        _tagsRepository = tagsRepository;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(RemoveAliasCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        TagId tagId = TagId.Of(command.TagId);
        TagId[] tagIds = TagId.Of(command.Request.TagIds);

        UnitResult<Error> beginTransactionResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (beginTransactionResult.IsFailure)
            return beginTransactionResult.Error;

        UnitResult<Error> lockResult = await _tagsRepository.AcquireMutationLocksAsync(
            [tagId, .. tagIds],
            cancellationToken);
        if (lockResult.IsFailure)
            return lockResult.Error;

        Result<Tag, Error> tagResult = await _tagsRepository.GetBy(t => t.Id == tagId, cancellationToken);

        if (tagResult.IsFailure)
            return tagResult.Error;

        if (tagResult.Value.Kind != TagKind.CANON)
            return Error.Validation("tags.canonical.required", "Целевой тег должен быть каноническим");

        if (!_user.IsOwnerOrAdmin(tagResult.Value.AuthorId))
        {
            _logger.LogWarning(
                "Authorization denied: User {UserId} attempted to remove aliases from tag {TagId} owned by {OwnerId}.",
                _user.UserId,
                tagId.Value,
                tagResult.Value.AuthorId);
            return Error.Authorization("tag.not_owned", "Тег принадлежит другому автору");
        }

        // Verify the caller owns all alias tags being detached — prevents removing aliases contributed by another author.
        IReadOnlyList<Guid> aliasAuthorIds = await _tagsRepository.GetTagAuthorIdsAsync(tagIds, cancellationToken);

        if (!_user.IsAdmin && aliasAuthorIds.Any(authorId => authorId != _user.UserId))
        {
            _logger.LogWarning(
                "Authorization denied: User {UserId} attempted to remove alias tags owned by other authors from {TagId}.",
                _user.UserId,
                tagId.Value);
            return Error.Authorization("tag.alias.not_owned", "Один или несколько алиас-тегов принадлежат другому автору");
        }

        Result<IReadOnlyList<TagId>, Error> removeAliasesResult =
            await _tagsRepository.RemoveAliasesAsync(tagId, tagIds, cancellationToken);

        if (removeAliasesResult.IsFailure)
            return removeAliasesResult.Error;

        if (removeAliasesResult.Value.Count != tagIds.Length)
            return Error.Validation(
                "tags.alias.not_attached",
                "Один или несколько алиасов не привязаны к указанному тегу");

        UnitResult<Error> markCanonResult =
            await _tagsRepository.MarkOrphanAliasesCanonAsync(removeAliasesResult.Value, cancellationToken);
        if (markCanonResult.IsFailure)
            return markCanonResult.Error;

        await _outboxService.PublishAsync(new TagAliasRemoved(
            tagId.Value,
            removeAliasesResult.Value.Select(x => x.Value).ToArray()));

        UnitResult<Error> saveResult = await _transactionManager.CommitTransactionAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Aliases have been removed for tag {TagId}. Count={Count}.", tagId.Value, tagIds.Length);

        return tagId.Value;
    }
}
