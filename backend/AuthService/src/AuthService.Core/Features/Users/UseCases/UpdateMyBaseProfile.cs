using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using AuthService.Contracts;
using AuthService.Core.Database;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using PlatformAuth.Middleware;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Users.UseCases;

public sealed record UpdateMyBaseProfileCommand(UpdateMyBaseProfileRequest Request) : ICommand;

public sealed class UpdateMyBaseProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/users/me/base", async Task<EndpointResult<Guid>> (
                    [FromBody] UpdateMyBaseProfileRequest request,
                    [FromServices] UpdateMyBaseProfileHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UpdateMyBaseProfileCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
}

public sealed class UpdateMyBaseProfileRequestValidator : AbstractValidator<UpdateMyBaseProfileCommand>
{
    public UpdateMyBaseProfileRequestValidator()
    {
        RuleFor(x => x.Request.Bio)
            .MustBeValueObject(value => Bio.Create(value!))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Bio));
    }
}

public sealed class UpdateMyBaseProfileHandler : ICommandHandler<Guid, UpdateMyBaseProfileCommand>
{
    private readonly IProfileRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateMyBaseProfileCommand> _validator;

    public UpdateMyBaseProfileHandler(
        IProfileRepository repository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<UpdateMyBaseProfileCommand> validator)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateMyBaseProfileCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();


        UserProfile? profile = await _repository.GetByAsync(x => x.Id == _user.UserId, cancellationToken);
        if (profile is null)
            return ProfileErrors.ProfileNotFound(_user.UserId);

        Bio? bio = Bio.CreateOptional(command.Request.Bio).Value;

        profile.UpdateBaseProfile(bio, DateTime.UtcNow);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return profile.Id;
    }
}
