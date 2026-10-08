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
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.AuthorSpaces.UseCases;

public sealed record UpdateAuthorSpaceSlugCommand(UpdateSlugRequest Request) : ICommand;

public sealed class UpdateAuthorSpaceSlugEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/users/me/author-space/slug", async Task<EndpointResult<Guid>> (
                    [FromBody] UpdateSlugRequest request,
                    [FromServices] UpdateAuthorSpaceSlugHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UpdateAuthorSpaceSlugCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Profiles.AUTHOR);
}

public sealed class UpdateAuthorSpaceSlugValidator : AbstractValidator<UpdateAuthorSpaceSlugCommand>
{
    public UpdateAuthorSpaceSlugValidator()
    {
        RuleFor(x => x.Request.Slug)
            .MustBeValueObject(value => AuthorSpaceSlug.Create(value));
    }
}

public sealed class UpdateAuthorSpaceSlugHandler : ICommandHandler<Guid, UpdateAuthorSpaceSlugCommand>
{
    private readonly IAuthorSpaceRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateAuthorSpaceSlugCommand> _validator;

    public UpdateAuthorSpaceSlugHandler(
        IAuthorSpaceRepository repository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<UpdateAuthorSpaceSlugCommand> validator)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateAuthorSpaceSlugCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        AuthorSpace? space = await _repository.GetByAsync(
            s => s.Id == _user.UserId, cancellationToken);
        if (space is null)
            return AuthorSpaceErrors.NotFound(_user.UserId);

        AuthorSpaceSlug newSlug = AuthorSpaceSlug.Create(command.Request.Slug).Value;

        // Check uniqueness (skip if unchanged)
        if (!string.Equals(space.Slug.Value, newSlug.Value, StringComparison.Ordinal)
            && await _repository.ExistsAsync(s => s.Slug == newSlug, cancellationToken))
        {
            return AuthorSpaceErrors.SlugTaken(newSlug.Value);
        }

        space.UpdateSlug(newSlug, DateTime.UtcNow);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return space.Id;
    }
}
