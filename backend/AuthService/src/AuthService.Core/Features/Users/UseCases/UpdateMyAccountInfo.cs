using AuthService.Contracts;
using AuthService.Core.Database;
using AuthService.Domain;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Features.Users.UseCases;

public sealed record UpdateMyAccountInfoCommand(UpdateMyAccountInfoRequest Request) : ICommand;

public sealed class UpdateMyAccountInfoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/users/me/account", async Task<EndpointResult<string>> (
                    [FromBody] UpdateMyAccountInfoRequest request,
                    [FromServices] UpdateMyAccountInfoHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UpdateMyAccountInfoCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
}

public sealed class UpdateMyAccountInfoRequestValidator : AbstractValidator<UpdateMyAccountInfoCommand>
{
    public UpdateMyAccountInfoRequestValidator()
    {
        RuleFor(x => x.Request.Username)
            .MinimumLength(3).WithError(GeneralErrors.ValueIsInvalid("username"))
            .MaximumLength(30).WithError(GeneralErrors.ValueIsInvalid("username"))
            .Matches(@"^[a-zA-Z0-9_\-.]+$").WithError(GeneralErrors.ValueIsInvalid("username"))
            .When(x => x.Request.Username is not null);

        RuleFor(x => x.Request.DisplayName)
            .Must(v => !string.IsNullOrWhiteSpace(v))
                .WithError(GeneralErrors.ValueIsInvalid("displayName"))
            .MaximumLength(50)
                .WithError(GeneralErrors.ValueIsInvalid("displayName"))
            .When(x => x.Request.DisplayName is not null);
    }
}

public sealed class UpdateMyAccountInfoHandler : ICommandHandler<string, UpdateMyAccountInfoCommand>
{
    private readonly UserScopedData _user;
    private readonly UserManager<Account> _userManager;
    private readonly IValidator<UpdateMyAccountInfoCommand> _validator;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;

    public UpdateMyAccountInfoHandler(
        UserScopedData user,
        UserManager<Account> userManager,
        IValidator<UpdateMyAccountInfoCommand> validator,
        IOutboxService outboxService,
        ITransactionManager transactionManager)
    {
        _user = user;
        _userManager = userManager;
        _validator = validator;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
    }

    public async Task<Result<string, Error>> Handle(
        UpdateMyAccountInfoCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        string? oldUsername = user.UserName;
        string? oldDisplayName = user.DisplayName;

        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        if (command.Request.Username is not null)
        {
            string newUsername = command.Request.Username.Trim();
            if (!string.Equals(oldUsername, newUsername, StringComparison.Ordinal))
            {
                IdentityResult result = await _userManager.SetUserNameAsync(user, newUsername);
                if (!result.Succeeded)
                    return AuthErrors.FromIdentityError(result.Errors.First());
            }
        }

        if (command.Request.DisplayName is not null)
            user.UpdateDisplayName(command.Request.DisplayName.Trim());

        // Ensure UpdatedAt is set even if DisplayName/UserName didn't change
        user.UpdatedAt = DateTime.UtcNow;

        IdentityResult updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return AuthErrors.FromIdentityError(updateResult.Errors.First());

        bool usernameChanged = !string.Equals(oldUsername, user.UserName, StringComparison.Ordinal);
        bool displayNameChanged = !string.Equals(oldDisplayName, user.DisplayName, StringComparison.Ordinal);

        if (usernameChanged)
            await _outboxService.PublishAsync(new UserUsernameUpdated(user.Id, user.UserName));

        if (displayNameChanged)
            await _outboxService.PublishAsync(new UserDisplayNameUpdated(user.Id, user.DisplayName));

        if (usernameChanged || displayNameChanged)
        {
            UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailure)
                return saveResult.Error;
        }

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        return "ok";
    }
}
