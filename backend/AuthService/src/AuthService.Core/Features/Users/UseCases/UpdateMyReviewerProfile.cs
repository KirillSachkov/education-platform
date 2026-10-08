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

public sealed record UpdateMyReviewerProfileCommand(UpdateMyReviewerProfileRequest Request) : ICommand;

public sealed class UpdateMyReviewerProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/users/me/reviewer", async Task<EndpointResult<Guid>> (
                    [FromBody] UpdateMyReviewerProfileRequest request,
                    [FromServices] UpdateMyReviewerProfileHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UpdateMyReviewerProfileCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Profiles.REVIEWER);
}

public sealed class UpdateMyReviewerProfileRequestValidator : AbstractValidator<UpdateMyReviewerProfileCommand>
{
    public UpdateMyReviewerProfileRequestValidator()
    {
        RuleFor(x => x.Request.ReviewCapacity)
            .GreaterThanOrEqualTo(0)
            .When(x => x.Request.ReviewCapacity.HasValue)
            .WithError(GeneralErrors.ValueIsInvalid("review capacity"));

        RuleFor(x => x.Request.Expertise)
            .MustBeValueObject(value => Expertise.Create(value!))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Expertise));
    }
}

public sealed class UpdateMyReviewerProfileHandler : ICommandHandler<Guid, UpdateMyReviewerProfileCommand>
{
    private readonly IProfileRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IValidator<UpdateMyReviewerProfileCommand> _validator;

    public UpdateMyReviewerProfileHandler(
        IProfileRepository repository,
        ITransactionManager transactionManager,
        UserScopedData user,
        IValidator<UpdateMyReviewerProfileCommand> validator)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateMyReviewerProfileCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();


        UserProfile? profile = await _repository.GetByAsync(x => x.Id == _user.UserId, cancellationToken);
        if (profile is null)
            return ProfileErrors.ProfileNotFound(_user.UserId);

        Expertise? expertise = Expertise.CreateOptional(command.Request.Expertise).Value;

        profile.UpdateReviewerProfile(
            command.Request.ReviewCapacity,
            expertise,
            DateTime.UtcNow);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return profile.Id;
    }
}
