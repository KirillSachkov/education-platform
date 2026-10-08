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
using TagService.Domain.EntityTags;
using TagService.Domain.TagAliases;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.UseCases;

public sealed class MergeTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/tags/{id:guid}/aliases", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid id,
            [FromBody] MergeTagsRequest request,
            [FromServices] MergeTagsHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new MergeTagsCommand(id, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class MergeTagsValidator : AbstractValidator<MergeTagsCommand>
{
    public MergeTagsValidator()
    {
        RuleFor(x => x.TagId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("tagId"));

        RuleFor(x => x.Request.TagIds)
            .Must(x => x.Count > 0)
            .WithError(Error.Validation("tags.aliases.empty", "Необходимо указать хотя бы один идентификатор тега"))
            .Must(x => x.Count == x.Distinct().Count())
            .WithError(Error.Validation("tags.aliases.duplicates", "Идентификаторы тегов-алиасов должны быть уникальными"))
            .Must(x => x.Count <= Constants.MAX_TAGS_PER_MUTATION)
            .WithError(Error.Validation(
                "tags.request.too_many",
                $"Допустимо не более {Constants.MAX_TAGS_PER_MUTATION} тегов за один запрос"));

        RuleFor(x => x)
            .Must(x => x.Request.TagIds.All(t => t != x.TagId))
            .WithError(Error.Validation("tags.merge.self", "Основной тег не должен быть среди алиасов"));
    }
}

public sealed record MergeTagsCommand(Guid TagId, MergeTagsRequest Request) : ICommand;

public sealed class MergeTagsHandler : ICommandHandler<Guid, MergeTagsCommand>
{
    private readonly ITagsRepository _tagsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly UserScopedData _user;
    private readonly IValidator<MergeTagsCommand> _validator;
    private readonly ILogger<MergeTagsHandler> _logger;

    public MergeTagsHandler(
        ITagsRepository tagsRepository,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        UserScopedData user,
        IValidator<MergeTagsCommand> validator,
        ILogger<MergeTagsHandler> logger)
    {
        _tagsRepository = tagsRepository;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(MergeTagsCommand command, CancellationToken cancellationToken)
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

        Result<Tag, Error> canonicalTagResult = await _tagsRepository.GetBy(t => t.Id == tagId, cancellationToken);

        if (canonicalTagResult.IsFailure)
            return canonicalTagResult.Error;

        Tag canonicalTag = canonicalTagResult.Value;

        if (canonicalTag.Kind != TagKind.CANON)
            return Error.Validation("tags.canonical.required", "Целевой тег должен быть каноническим");

        if (!_user.IsOwnerOrAdmin(canonicalTag.AuthorId))
        {
            _logger.LogWarning(
                "Authorization denied: User {UserId} attempted to merge into tag {TagId} owned by {OwnerId}.",
                _user.UserId,
                tagId.Value,
                canonicalTag.AuthorId);
            return Error.Authorization("tag.not_owned", "Тег принадлежит другому автору");
        }

        bool allAliasTagsExist = await _tagsRepository.CheckAllTagsExistAsync(tagIds, cancellationToken);

        if (!allAliasTagsExist)
            return Error.NotFound("tags.notfound", "Один или несколько указанных тегов не найдены");

        // Verify the caller owns all alias tags being absorbed — prevents cross-author tag hijacking.
        IReadOnlyList<Guid> aliasAuthorIds = await _tagsRepository.GetTagAuthorIdsAsync(tagIds, cancellationToken);

        if (!_user.IsAdmin && aliasAuthorIds.Any(authorId => authorId != _user.UserId))
        {
            _logger.LogWarning(
                "Authorization denied: User {UserId} attempted to merge alias tags owned by other authors into {TagId}.",
                _user.UserId,
                tagId.Value);
            return Error.Authorization("tag.alias.not_owned", "Один или несколько алиас-тегов принадлежат другому автору");
        }

        bool hasAnyAlias = await _tagsRepository.HasAnyAlias(tagIds, cancellationToken);

        if (hasAnyAlias)
            return Error.Validation("tags.request.alias", "Нельзя добавить алиас в качестве алиаса");

        UnitResult<Error> markAliasResult = await _tagsRepository.MarkTagsAliasAsync(tagIds, cancellationToken);

        if (markAliasResult.IsFailure)
            return markAliasResult.Error;

        List<TagAlias> aliasesToCreate = tagIds
            .Select(aliasTagId => TagAlias.Create(tagId, aliasTagId).Value)
            .ToList();

        await _tagsRepository.AddAliasesAsync(aliasesToCreate, cancellationToken);

        UnitResult<Error> replaceResult = await _tagsRepository.MergeEntityTagsAsync(tagId, tagIds, cancellationToken);

        if (replaceResult.IsFailure)
            return replaceResult.Error;

        await _outboxService.PublishAsync(new TagsMerged(tagId.Value, command.Request.TagIds));

        // CommitTransactionAsync internally saves EF changes + flushes the outbox atomically.
        // Calling SaveChangesAsync separately would only flush EF state and silently drop the
        // outbox envelope on failure (DB rolled back but no error path for the publish).
        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);

        if (commitResult.IsFailure)
            return commitResult.Error;

        _logger.LogInformation(
            "Aliases have been linked to tag {TagId}. Added={AliasesCount}.",
            tagId.Value,
            aliasesToCreate.Count);

        return tagId.Value;
    }
}
