using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Tags.Events;
using TagService.Contracts.Tags.Requests;
using TagService.Core.Database;
using TagService.Domain.EntityTags;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.UseCases;

public sealed class RemoveTagEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/tags/entity", async Task<EndpointResult<Guid>> (
            [FromBody] RemoveTagRequest request,
            [FromServices] RemoveTagHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new RemoveTagCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class RemoveTagValidator : AbstractValidator<RemoveTagCommand>
{
    public RemoveTagValidator()
    {
        RuleFor(x => x.Request)
            .MustBeValueObject(x => TagEntityReference.Of(x.EntityType, x.EntityId));

        RuleFor(x => x.Request.TagIds)
            .Must(x => x.Count > 0)
            .WithError(Error.Validation("tags.request.empty", "Необходимо указать хотя бы один идентификатор тега"))
            .Must(tagIds => tagIds.Count == tagIds.Distinct().Count())
            .WithError(Error.Validation("tags.request.tagids.duplicates", "Идентификаторы тегов должны быть уникальными"))
            .Must(tagIds => tagIds.Count <= Constants.MAX_TAGS_PER_MUTATION)
            .WithError(Error.Validation(
                "tags.request.too_many",
                $"Допустимо не более {Constants.MAX_TAGS_PER_MUTATION} тегов за один запрос"));
    }
}

public sealed record RemoveTagCommand(RemoveTagRequest Request) : ICommand;

public sealed class RemoveTagHandler : ICommandHandler<Guid, RemoveTagCommand>
{
    private readonly EntityTagAuthorization _entityAuthorization;
    private readonly ITagsRepository _tagsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly IValidator<RemoveTagCommand> _validator;
    private readonly ILogger<RemoveTagHandler> _logger;

    public RemoveTagHandler(
        EntityTagAuthorization entityAuthorization,
        ITagsRepository tagsRepository,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        IValidator<RemoveTagCommand> validator,
        ILogger<RemoveTagHandler> logger)
    {
        _entityAuthorization = entityAuthorization;
        _tagsRepository = tagsRepository;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(RemoveTagCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        TagEntityReference entityReference = TagEntityReference.Of(command.Request.EntityType, command.Request.EntityId).Value;
        string entityType = entityReference.Type.ToString().ToLowerInvariant();

        UnitResult<Error> authorizationResult = await _entityAuthorization.CheckCanManageAsync(
            entityType,
            entityReference.Id,
            cancellationToken);
        if (authorizationResult.IsFailure)
            return authorizationResult.Error;

        TagId[] tagIds = TagId.Of(command.Request.TagIds);

        UnitResult<Error> beginTransactionResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (beginTransactionResult.IsFailure)
            return beginTransactionResult.Error;

        Result<IReadOnlyList<TagId>, Error> removeTagsResult =
            await _tagsRepository.RemoveTagsFromEntityAsync(entityReference, tagIds, cancellationToken);

        if (removeTagsResult.IsFailure)
            return removeTagsResult.Error;

        if (removeTagsResult.Value.Count > 0)
        {
            await _outboxService.PublishAsync(new TagsRemovedFromEntity(
                entityReference.Id,
                entityReference.Type,
                removeTagsResult.Value.Select(x => x.Value).ToArray()));
        }

        UnitResult<Error> saveResult = await _transactionManager.CommitTransactionAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Tags have been removed for entity {EntityType}:{EntityId}. Count={TagIdsCount}.",
            entityType,
            entityReference.Id,
            removeTagsResult.Value.Count);

        return entityReference.Id;
    }
}
