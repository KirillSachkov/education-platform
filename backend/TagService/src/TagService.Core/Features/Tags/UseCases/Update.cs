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

public sealed class UpdateTagEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/tags/{id:guid}", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid id,
            [FromBody] UpdateTagRequest request,
            [FromServices] UpdateTagHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new UpdateTagCommand(id, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class UpdateTagValidator : AbstractValidator<UpdateTagCommand>
{
    public UpdateTagValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("tagId"));

        RuleFor(x => x.Request.Title)
            .MustBeValueObject(TagTitle.Of);
    }
}

public sealed record UpdateTagCommand(Guid Id, UpdateTagRequest Request) : ICommand;

public sealed class UpdateTagHandler : ICommandHandler<Guid, UpdateTagCommand>
{
    private readonly ITagsRepository _tagsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outboxService;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateTagCommand> _validator;
    private readonly ILogger<UpdateTagHandler> _logger;

    public UpdateTagHandler(
        ITagsRepository tagsRepository,
        ITransactionManager transactionManager,
        IOutboxService outboxService,
        UserScopedData user,
        IValidator<UpdateTagCommand> validator,
        ILogger<UpdateTagHandler> logger)
    {
        _tagsRepository = tagsRepository;
        _transactionManager = transactionManager;
        _outboxService = outboxService;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(UpdateTagCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        TagId tagId = TagId.Of(command.Id);

        Result<Tag, Error> tagResult = await _tagsRepository.GetBy(x => x.Id == tagId, cancellationToken);

        if (tagResult.IsFailure)
            return tagResult.Error;

        Tag tag = tagResult.Value;

        if (!_user.IsOwnerOrAdmin(tag.AuthorId))
        {
            _logger.LogWarning(
                "Authorization denied: User {UserId} attempted to update tag {TagId} owned by {OwnerId}.",
                _user.UserId,
                tagId.Value,
                tag.AuthorId);
            return Error.Authorization("tag.not_owned", "Тег принадлежит другому автору");
        }

        TagTitle title = TagTitle.Of(command.Request.Title).Value;

        Result<TagSlug, Error> slugResult = TagSlug.Of(command.Request.Title);

        if (slugResult.IsFailure)
            return slugResult.Error;

        TagSlug slug = slugResult.Value;

        tag.Update(title, slug);

        await _outboxService.PublishAsync(new TagUpdated(tag.Id.Value));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Tag {TagId} ({TagTitle}) has been updated.", tagId.Value, tag.Title.Value);

        return tagId.Value;
    }
}
