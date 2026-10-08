using AuthService.Contracts.AuthorSpaces;
using AuthService.Core.Database;
using AuthService.Domain.AuthorSpaces;
using AuthService.Domain.ValueObjects;
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

namespace AuthService.Core.Features.AuthorSpaces.UseCases;

public sealed record CreateAuthorSpaceCommand(CreateAuthorSpaceRequest Request) : ICommand;

public sealed class CreateAuthorSpaceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/admin/author-spaces", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateAuthorSpaceRequest request,
                    [FromServices] CreateAuthorSpaceHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new CreateAuthorSpaceCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE);
}

public sealed class CreateAuthorSpaceValidator : AbstractValidator<CreateAuthorSpaceCommand>
{
    public CreateAuthorSpaceValidator()
    {
        RuleFor(x => x.Request.UserId)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("userId"));

        RuleFor(x => x.Request.Slug)
            .MustBeValueObject(value => AuthorSpaceSlug.Create(value));
    }
}

public sealed class CreateAuthorSpaceHandler : ICommandHandler<Guid, CreateAuthorSpaceCommand>
{
    private readonly IAuthorSpaceRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<CreateAuthorSpaceCommand> _validator;

    public CreateAuthorSpaceHandler(
        IAuthorSpaceRepository repository,
        ITransactionManager transactionManager,
        IValidator<CreateAuthorSpaceCommand> validator)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateAuthorSpaceCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        AuthorSpace? existing = await _repository.GetByAsync(
            s => s.Id == command.Request.UserId, cancellationToken);

        if (existing is not null)
            return AuthorSpaceErrors.AlreadyExists(command.Request.UserId);

        AuthorSpaceSlug slug = AuthorSpaceSlug.Create(command.Request.Slug).Value;

        if (await _repository.ExistsAsync(s => s.Slug == slug, cancellationToken))
            return AuthorSpaceErrors.SlugTaken(slug.Value);

        DateTime now = DateTime.UtcNow;

        var space = new AuthorSpace
        {
            Id = command.Request.UserId,
            Slug = slug,
            FeatureFlags = new AuthorSpaceFeatureFlags(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await _repository.AddAsync(space, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return space.Id;
    }
}
