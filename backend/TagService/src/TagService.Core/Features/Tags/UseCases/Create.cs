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
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;

namespace TagService.Core.Features.Tags.UseCases;

public sealed class CreateTagEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/tags", async Task<EndpointResult<Guid>> (
            [FromBody] CreateTagRequest request,
            [FromServices] CreateTagHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new CreateTagCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class CreateTagValidator : AbstractValidator<CreateTagCommand>
{
    public CreateTagValidator()
    {
        RuleFor(x => x.Request.Title)
            .MustBeValueObject(TagTitle.Of);
    }
}

public sealed record CreateTagCommand(CreateTagRequest Request) : ICommand;

public sealed class CreateTagHandler : ICommandHandler<Guid, CreateTagCommand>
{
    private readonly ITagsRepository _tagsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<CreateTagCommand> _validator;
    private readonly ILogger<CreateTagHandler> _logger;

    public CreateTagHandler(
        ITagsRepository tagsRepository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<CreateTagCommand> validator,
        ILogger<CreateTagHandler> logger)
    {
        _tagsRepository = tagsRepository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(CreateTagCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
            return validationResult.ToError();

        TagTitle title = TagTitle.Of(command.Request.Title).Value;

        Result<TagSlug, Error> slugResult = TagSlug.Of(command.Request.Title);

        if (slugResult.IsFailure)
            return slugResult.Error;

        TagSlug slug = slugResult.Value;

        Result<Tag, Error> tagResult = Tag.Create(title, slug, _user.UserId);

        if (tagResult.IsFailure)
            return tagResult.Error;

        Tag newTag = tagResult.Value;

        await _tagsRepository.AddAsync(newTag, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);

        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Tag {TagId} ({TagTitle}) has been created by author {AuthorId}.",
            newTag.Id.Value, newTag.Title.Value, _user.UserId);

        return newTag.Id.Value;
    }
}
