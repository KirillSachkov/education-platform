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

public sealed record CompleteProfileCommand(CompleteProfileRequest Request) : ICommand;

public sealed class CompleteProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/me/profile/complete", async Task<EndpointResult<string>> (
                    [FromBody] CompleteProfileRequest request,
                    [FromServices] CompleteProfileHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new CompleteProfileCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
}

public sealed class CompleteProfileCommandValidator : AbstractValidator<CompleteProfileCommand>
{
    public CompleteProfileCommandValidator()
    {
        RuleFor(x => x.Request.DisplayName)
            .MaximumLength(50).WithError(GeneralErrors.ValueIsInvalid("displayName"));
    }
}

public sealed class CompleteProfileHandler : ICommandHandler<string, CompleteProfileCommand>
{
    private readonly UserScopedData _user;
    private readonly UserManager<Account> _userManager;
    private readonly IValidator<CompleteProfileCommand> _validator;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;

    public CompleteProfileHandler(
        UserScopedData user,
        UserManager<Account> userManager,
        IValidator<CompleteProfileCommand> validator,
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
        CompleteProfileCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        // DisplayName опциональный — пустая/whitespace строка означает «не задавать имя».
        string? displayName = string.IsNullOrWhiteSpace(command.Request.DisplayName)
            ? null
            : command.Request.DisplayName.Trim();
        string? oldDisplayName = user.DisplayName;

        // Idempotent: if no actual change (incl. null → null when user re-submits empty
        // form), early-return without DB write or event publish.
        bool displayNameChanged = !string.Equals(oldDisplayName, displayName, StringComparison.Ordinal);
        if (!displayNameChanged)
            return "ok";

        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        user.UpdateDisplayName(displayName);

        IdentityResult updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return AuthErrors.FromIdentityError(updateResult.Errors.First());

        await _outboxService.PublishAsync(new UserDisplayNameUpdated(user.Id, user.DisplayName));

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        return "ok";
    }
}
