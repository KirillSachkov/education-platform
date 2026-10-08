using AuthService.Contracts.Admin;
using AuthService.Core.Database;
using AuthService.Core.Features.Admin.Audit;
using AuthService.Core.Validation;
using AuthService.Core.Services;
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
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Features.Admin.UseCases;

public sealed record AdminCreateUserCommand(AdminCreateUserRequest Request) : ICommand;

public sealed class AdminCreateUserValidator : AbstractValidator<AdminCreateUserCommand>
{
    public AdminCreateUserValidator()
    {
        RuleFor(x => x.Request.Email)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("email"))
            .EmailAddress().WithError(GeneralErrors.ValueIsInvalid("email"));

        RuleFor(x => x.Request.Password)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("password"))
            .IsValidPassword();

        RuleFor(x => x.Request.Username)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("username"))
            .MinimumLength(3).WithError(GeneralErrors.ValueIsInvalid("username"))
            .MaximumLength(30).WithError(GeneralErrors.ValueIsInvalid("username"))
            .Matches(@"^[a-zA-Z0-9_\-.]+$").WithError(GeneralErrors.ValueIsInvalid("username"));

        RuleFor(x => x.Request.Roles)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("roles"));

        RuleForEach(x => x.Request.Roles)
            .Must(r => PlatformRoles.All.Contains(r))
            .WithError(GeneralErrors.ValueIsInvalid("roles"));
    }
}

public sealed class AdminCreateUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/", async Task<EndpointResult<Guid>> (
                    [FromBody] AdminCreateUserRequest request,
                    [FromServices] AdminCreateUserHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminCreateUserCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserCreated);
}

public sealed class AdminCreateUserHandler : ICommandHandler<Guid, AdminCreateUserCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly IProfileRepository _profileRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<AdminCreateUserCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;
    private readonly IOutboxService _outboxService;

    public AdminCreateUserHandler(
        UserManager<Account> userManager,
        IProfileRepository profileRepository,
        ITransactionManager transactionManager,
        IValidator<AdminCreateUserCommand> validator,
        ILogger<AuthAudit> audit,
        IOutboxService outboxService)
    {
        _userManager = userManager;
        _profileRepository = profileRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _audit = audit;
        _outboxService = outboxService;
    }

    public async Task<Result<Guid, Error>> Handle(
        AdminCreateUserCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        string email = command.Request.Email.Trim().ToLowerInvariant();

        Account? existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
            return AuthErrors.EmailTaken();

        var user = new Account
        {
            Id = Guid.CreateVersion7(),
            UserName = command.Request.Username.Trim(),
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        IdentityResult createResult = await _userManager.CreateAsync(user, command.Request.Password);
        if (!createResult.Succeeded)
            return AuthErrors.FromIdentityError(createResult.Errors.First());

        IdentityResult rolesResult = await _userManager.AddToRolesAsync(user, command.Request.Roles);
        if (!rolesResult.Succeeded)
            return AuthErrors.FromIdentityError(rolesResult.Errors.First());

        await _profileRepository.EnsureExistsAsync(user.Id, cancellationToken);
        await _outboxService.PublishAsync(new UserCreated(user.Id, user.UserName, user.DisplayName));

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        _audit.LogRegistration(user.Id, "admin");

        return user.Id;
    }
}
