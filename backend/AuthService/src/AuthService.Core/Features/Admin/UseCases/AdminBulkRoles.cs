using AuthService.Contracts.Admin;
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

public sealed record AdminBulkRolesCommand(AdminBulkRolesRequest Request) : ICommand;

public sealed class AdminBulkRolesValidator : AbstractValidator<AdminBulkRolesCommand>
{
    public const int MAX_BATCH_SIZE = 500;

    public AdminBulkRolesValidator()
    {
        RuleFor(x => x.Request.UserIds)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("userIds"));

        RuleFor(x => x.Request.UserIds)
            .Must(ids => ids.Count <= MAX_BATCH_SIZE)
            .WithError(GeneralErrors.ValueIsInvalid("userIds"));

        RuleFor(x => x.Request)
            .Must(r => r.Add.Count > 0 || r.Remove.Count > 0)
            .WithError(GeneralErrors.ValueIsRequired("add|remove"));

        // Имена ролей — против whitelist'а PlatformRoles (зеркалит AdminSetRolesValidator).
        // Без этого админ мог инжектить произвольные строки в Identity role-store.
        RuleForEach(x => x.Request.Add)
            .Must(r => PlatformRoles.All.Contains(r))
            .WithError(GeneralErrors.ValueIsInvalid("add"));

        RuleForEach(x => x.Request.Remove)
            .Must(r => PlatformRoles.All.Contains(r))
            .WithError(GeneralErrors.ValueIsInvalid("remove"));
    }
}

public sealed class AdminBulkRolesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/admin/bulk/roles", async Task<EndpointResult<AdminBulkActionResponse>> (
                    [FromBody] AdminBulkRolesRequest request,
                    [FromServices] AdminBulkRolesHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminBulkRolesCommand(request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserBulkRoles);
}

public sealed class AdminBulkRolesHandler : ICommandHandler<AdminBulkActionResponse, AdminBulkRolesCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly UserScopedData _user;
    private readonly IValidator<AdminBulkRolesCommand> _validator;

    public AdminBulkRolesHandler(
        UserManager<Account> userManager,
        TokenRevocationService tokenRevocation,
        UserScopedData user,
        IValidator<AdminBulkRolesCommand> validator)
    {
        _userManager = userManager;
        _tokenRevocation = tokenRevocation;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<AdminBulkActionResponse, Error>> Handle(
        AdminBulkRolesCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        List<Guid> succeeded = [];
        List<AdminBulkActionFailureDto> failed = [];

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

            bool rolesChanged = false;
            try
            {
                if (command.Request.Remove.Count > 0)
                {
                    IdentityResult removeResult = await _userManager.RemoveFromRolesAsync(
                        account, command.Request.Remove);
                    if (!removeResult.Succeeded)
                    {
                        failed.Add(new AdminBulkActionFailureDto(
                            targetId,
                            removeResult.Errors.FirstOrDefault()?.Description ?? "remove.failed"));
                        continue;
                    }

                    rolesChanged = true;
                }

                if (command.Request.Add.Count > 0)
                {
                    IdentityResult addResult = await _userManager.AddToRolesAsync(
                        account, command.Request.Add);
                    if (!addResult.Succeeded)
                    {
                        if (rolesChanged)
                        {
                            await _tokenRevocation.RevokeAllUserTokensAsync(
                                targetId, cancellationToken);
                        }

                        failed.Add(new AdminBulkActionFailureDto(
                            targetId,
                            addResult.Errors.FirstOrDefault()?.Description ?? "add.failed"));
                        continue;
                    }

                    rolesChanged = true;
                }

                if (rolesChanged)
                    await _tokenRevocation.RevokeAllUserTokensAsync(targetId, cancellationToken);

                succeeded.Add(targetId);
            }
            catch (InvalidOperationException ex)
            {
                if (rolesChanged)
                    await _tokenRevocation.RevokeAllUserTokensAsync(targetId, cancellationToken);

                failed.Add(new AdminBulkActionFailureDto(targetId, ex.Message));
            }
        }

        return new AdminBulkActionResponse(succeeded, failed);
    }
}
