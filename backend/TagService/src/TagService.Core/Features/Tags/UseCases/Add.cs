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
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Tags.Events;
using TagService.Contracts.Tags.Requests;
using TagService.Core.Database;
using TagService.Domain.EntityTags;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.UseCases;

public sealed class AddTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/tags/entity", async Task<EndpointResult<Guid>> (
            [FromBody] AddTagsRequest request,
            [FromServices] AddTagsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new AddTagsCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class AddTagsValidator : AbstractValidator<AddTagsCommand>
{
    public AddTagsValidator()
    {
        RuleFor(x => x.Request)
            .MustBeValueObject(x => TagEntityReference.Of(x.EntityType, x.EntityId));

        RuleFor(x => x.Request.TagIds
                .Select(id => id.ToString())
                .Concat(x.Request.TagTitles)
                .ToList())
            .Must(tags => tags.Count > 0)
            .WithError(Error.Validation("tags.request.empty", "Необходимо указать хотя бы один тег"))
            .Must(tags => tags.Count == tags.Distinct().Count())
            .WithError(Error.Validation("tags.request.duplicates", "Теги должны быть уникальными"))
            .Must(tags => tags.Count <= Constants.MAX_TAGS_PER_MUTATION)
            .WithError(Error.Validation(
                "tags.request.too_many",
                $"Допустимо не более {Constants.MAX_TAGS_PER_MUTATION} тегов за один запрос"));

        RuleForEach(x => x.Request.TagTitles)
            .MustBeValueObject(TagTitle.Of);

        RuleForEach(x => x.Request.TagTitles)
            .MustBeValueObject(TagSlug.Of);
    }
}

public sealed record AddTagsCommand(AddTagsRequest Request) : ICommand;

public sealed class AddTagsHandler : ICommandHandler<Guid, AddTagsCommand>
{
    private readonly EntityTagAuthorization _entityAuthorization;
    private readonly ITagsRepository _tagsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly UserScopedData _user;
    private readonly IValidator<AddTagsCommand> _validator;
    private readonly ILogger<AddTagsHandler> _logger;

    public AddTagsHandler(
        EntityTagAuthorization entityAuthorization,
        ITagsRepository tagsRepository,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        UserScopedData user,
        IValidator<AddTagsCommand> validator,
        ILogger<AddTagsHandler> logger)
    {
        _entityAuthorization = entityAuthorization;
        _tagsRepository = tagsRepository;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(AddTagsCommand command, CancellationToken cancellationToken)
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

        List<TagId> canonTagIds = [];

        if (tagIds.Length > 0)
        {
            bool allTagsExist = await _tagsRepository.CheckAllTagsExistAsync(tagIds, cancellationToken);
            if (!allTagsExist)
                return Error.NotFound("tags.notfound", "Один или несколько указанных тегов не найдены");

            IReadOnlyList<Guid> authorIds = await _tagsRepository.GetTagAuthorIdsAsync(tagIds, cancellationToken);
            if (!_user.IsAdmin && authorIds.Any(authorId => authorId != _user.UserId))
                return Error.Authorization("tag.not_owned", "Один или несколько тегов принадлежат другому автору");

            IReadOnlyList<TagId> resolvedTagIds = await _tagsRepository.GetCanonTagIdsByIdsAsync(
                tagIds,
                _user.IsAdmin ? null : _user.UserId,
                cancellationToken);
            if (!_user.IsAdmin && resolvedTagIds.Count != tagIds.Length)
                return Error.Authorization("tag.not_owned", "Один или несколько тегов принадлежат другому автору");

            canonTagIds.AddRange(resolvedTagIds);
        }

        IReadOnlyList<CanonTagByTitle> canonTagIdsFromTitles =
            await _tagsRepository.GetCanonTagIdsByTitlesAsync(
                command.Request.TagTitles,
                _user.UserId,
                cancellationToken);

        canonTagIds.AddRange(canonTagIdsFromTitles.Select(x => TagId.Of(x.Id)));

        HashSet<string> matchedTitles = canonTagIdsFromTitles
            .Select(x => x.Title)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> titlesToCreate = command.Request.TagTitles
            .Where(title => !matchedTitles.Contains(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<Tag> tagsToCreate = titlesToCreate
            .Select(title => Tag.Create(TagTitle.Of(title).Value, TagSlug.Of(title).Value, _user.UserId).Value)
            .ToList();

        if (tagsToCreate.Count > 0)
        {
            await _tagsRepository.AddRangeAsync(tagsToCreate, cancellationToken);
            canonTagIds.AddRange(tagsToCreate.Select(x => x.Id));
        }

        EntityTag[] entityTags = canonTagIds.Distinct()
            .Select(tagId => EntityTag.Create(
                TagEntityReference.Of(entityReference.Type, entityReference.Id).Value,
                tagId).Value)
            .ToArray();

        await _tagsRepository.AddTagsToEntityAsync(entityTags, cancellationToken);

        await _outboxService.PublishAsync(new TagsAddedToEntity(
            entityReference.Id,
            entityReference.Type,
            entityTags.Select(x => x.TagId.Value).ToArray()));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Tags have been added for entity {EntityType}:{EntityId}.",
            entityType,
            entityReference.Id);

        return entityReference.Id;
    }
}
