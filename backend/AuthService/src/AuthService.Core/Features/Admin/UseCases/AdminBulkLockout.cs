using AuthService.Contracts.Admin;
using AuthService.Core.Database;
using AuthService.Core.Features.Admin.Audit;
using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
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

namespace AuthService.Core.Features.Admin.UseCases;

public sealed record AdminBulkLockoutCommand(AdminBulkLockoutRequest Request) : ICommand;

public sealed class AdminBulkLockoutValidator : AbstractValidator<AdminBulkLockoutCommand>
{
    public const int MAX_BATCH_SIZE = 500;

    public AdminBulkLockoutValidator()
    {
        RuleFor(x => x.Request.UserIds)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("userIds"));

        RuleFor(x => x.Request.UserIds)
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithError(GeneralErrors.ValueIsInvalid("userIds"));

        RuleFor(x => x.Request.LockoutEnd)
            .Must(end => end > DateTimeOffset.UtcNow)
            .WithError(GeneralErrors.ValueIsInvalid("lockoutEnd"))
            .When(x => x.Request.IsLocked && x.Request.LockoutEnd is not null);
    }
}

public sealed class AdminBulkLockoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/admin/bulk/lockout", async Task<EndpointResult<AdminBulkActionResponse>> (
                    [FromBody] AdminBulkLockoutRequest request,
                    [FromServices] AdminBulkLockoutHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminBulkLockoutCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserBulkLockout);
}

public sealed class AdminBulkLockoutHandler : ICommandHandler<AdminBulkActionResponse, AdminBulkLockoutCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly UserScopedData _user;
    private readonly IValidator<AdminBulkLockoutCommand> _validator;

    public AdminBulkLockoutHandler(
        UserManager<Account> userManager,
        TokenRevocationService tokenRevocation,
        UserScopedData user,
        IValidator<AdminBulkLockoutCommand> validator)
    {
        _userManager = userManager;
        _tokenRevocation = tokenRevocation;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<AdminBulkActionResponse, Error>> Handle(
        AdminBulkLockoutCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        List<Guid> succeeded = [];
        List<AdminBulkActionFailureDto> failed = [];

        DateTimeOffset? lockoutEnd = command.Request.IsLocked
            ? command.Request.LockoutEnd ?? DateTimeOffset.MaxValue
            : null;

        foreach (Guid targetId in command.Request.UserIds.Distinct())
        {
            if (targetId == _user.UserId)
            {
                failed.Add(new AdminBulkActionFailureDto(targetId, "cannot.modify.self"));
                continue;
            }

            Account? account = await _userManager.FindByIdAsync(targetId.ToString());
            if (account is null)
            {
                failed.Add(new AdminBulkActionFailureDto(targetId, "user.not.found"));
                continue;
            }

            IdentityResult enabledResult = await _userManager.SetLockoutEnabledAsync(account, true);
            if (!enabledResult.Succeeded)
            {
                failed.Add(new AdminBulkActionFailureDto(
                    targetId,
                    enabledResult.Errors.FirstOrDefault()?.Description ?? "identity.error"));
                continue;
            }

            IdentityResult result = await _userManager.SetLockoutEndDateAsync(account, lockoutEnd);
            if (!result.Succeeded)
            {
                failed.Add(new AdminBulkActionFailureDto(
                    targetId,
                    result.Errors.FirstOrDefault()?.Description ?? "identity.error"));
                continue;
            }

            if (command.Request.IsLocked)
                await _tokenRevocation.RevokeAllUserTokensAsync(targetId, cancellationToken);

            succeeded.Add(targetId);
        }

        return new AdminBulkActionResponse(succeeded, failed);
    }
}
