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

public sealed record UpdateMyAuthorProfileCommand(UpdateMyAuthorProfileRequest Request) : ICommand;

public sealed class UpdateMyAuthorProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/users/me/author", async Task<EndpointResult<Guid>> (
                    [FromBody] UpdateMyAuthorProfileRequest request,
                    [FromServices] UpdateMyAuthorProfileHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UpdateMyAuthorProfileCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Profiles.AUTHOR);
}

public sealed class UpdateMyAuthorProfileCommandValidator : AbstractValidator<UpdateMyAuthorProfileCommand>
{
    public UpdateMyAuthorProfileCommandValidator()
    {
        RuleFor(x => x.Request.Specialization)
            .MustBeValueObject(value => Specialization.Create(value!))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Specialization));

        RuleFor(x => x.Request.AboutAsAuthor)
            .MustBeValueObject(value => AboutAsAuthor.Create(value!))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.AboutAsAuthor));
    }
}

public sealed class UpdateMyAuthorProfileHandler : ICommandHandler<Guid, UpdateMyAuthorProfileCommand>
{
    private readonly IProfileRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateMyAuthorProfileCommand> _validator;

    public UpdateMyAuthorProfileHandler(
        IProfileRepository repository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<UpdateMyAuthorProfileCommand> validator)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateMyAuthorProfileCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        UserProfile? profile = await _repository.GetByAsync(x => x.Id == _user.UserId, cancellationToken);
        if (profile is null)
            return ProfileErrors.ProfileNotFound(_user.UserId);

        Specialization? specialization = Specialization.CreateOptional(command.Request.Specialization).Value;
        AboutAsAuthor? aboutAsAuthor = AboutAsAuthor.CreateOptional(command.Request.AboutAsAuthor).Value;

        profile.UpdateAuthorProfile(
            specialization,
            aboutAsAuthor,
            DateTime.UtcNow);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return profile.Id;
    }
}
