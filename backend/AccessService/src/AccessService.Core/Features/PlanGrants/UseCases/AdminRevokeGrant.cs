using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Core.Database;
using AccessService.Core.Features.Plans;
using AccessService.Domain;
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
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
/// POST /access/admin/grants/{grantId}/revoke (#414) — ADMIN отзывает ЛЮБОЙ grant любого
/// пользователя, без ownership-проверки (в отличие от author-scoped
/// <see cref="RevokeGrantEndpoint"/>). Owner decision: отзыв плана — ручной admin-процесс.
///
/// Использует тот же доменный путь <see cref="PlanGrant.Revoke"/>, поэтому публикуется
/// <c>plan_grant.revoked</c> (Redis content-access sync уже его потребляет).
///
/// Reason обязателен (для аудита). Идемпотентно для уже REVOKED grant'а — возвращает
/// его id без повторной публикации. 404 если grant не найден.
/// </summary>
public sealed class AdminRevokeGrantEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/admin/grants/{grantId:guid}/revoke", async Task<EndpointResult<Guid>> (
                [FromRoute] Guid grantId,
                [FromBody] AdminRevokeGrantRequest request,
                [FromServices] AdminRevokeGrantHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new AdminRevokeGrantCommand(grantId, request), ct))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed record AdminRevokeGrantCommand(Guid GrantId, AdminRevokeGrantRequest Request) : ICommand;

public sealed class AdminRevokeGrantCommandValidator : AbstractValidator<AdminRevokeGrantCommand>
{
    public AdminRevokeGrantCommandValidator()
    {
        RuleFor(x => x.Request.Reason)
            .NotEmpty()
            .WithError(Error.Validation("grant.revoke.reason.required", "Причина отзыва обязательна для аудита"));

        RuleFor(x => x.Request.Reason)
            .MaximumLength(PlanGrant.REVOKE_REASON_MAX_LENGTH)
            .WithError(Error.Validation("grant.revoke.reason.too_long", "Причина отзыва слишком длинная (>500 символов)"));
    }
}

public sealed class AdminRevokeGrantHandler : ICommandHandler<Guid, AdminRevokeGrantCommand>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<AdminRevokeGrantCommand> _validator;
    private readonly ILogger<AdminRevokeGrantHandler> _logger;

    public AdminRevokeGrantHandler(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<AdminRevokeGrantCommand> validator,
        ILogger<AdminRevokeGrantHandler> logger)
    {
        _grants = grants;
        _plans = plans;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        AdminRevokeGrantCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<PlanGrant, Error> getGrant = await _grants.GetByAsync(
            g => g.Id == command.GrantId, cancellationToken);
        if (getGrant.IsFailure)
        {
            // GetByAsync возвращает GeneralErrors.NotFound() — 404.
            return getGrant.Error;
        }

        PlanGrant grant = getGrant.Value;

        // Идемпотентность: уже REVOKED или EXPIRED — доступ уже снят, no-op без повторного
        // publish'а. Раньше EXPIRED проваливался в Revoke() → grant.not.active → 409, ломая
        // admin-tooling для естественно истёкших grant'ов.
        if (grant.Status is PlanGrantStatus.REVOKED or PlanGrantStatus.EXPIRED)
        {
            _logger.LogInformation(
                "Admin revoke idempotent hit: grant {GrantId} already {Status}",
                grant.Id,
                grant.Status);
            return grant.Id;
        }

        Result<Guid?, Error> canonicalPlanResult = await CanonicalTelegramPlanResolver.ResolveAsync(
            grant.PlanId,
            _plans,
            cancellationToken);
        if (canonicalPlanResult.IsFailure)
            return canonicalPlanResult.Error;

        UnitResult<Error> revoke = grant.Revoke(_user.UserId, command.Request.Reason);
        if (revoke.IsFailure)
        {
            return revoke.Error;
        }

        await _outbox.PublishAsync(new PlanGrantRevoked(
            grant.Id,
            grant.UserId,
            grant.PlanId,
            command.Request.Reason,
            grant.RevokedAt!.Value,
            canonicalPlanResult.Value));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Admin {Actor} revoked grant {GrantId} of user {UserId} (plan {PlanId}, reason {Reason})",
            _user.UserId, grant.Id, grant.UserId, grant.PlanId, command.Request.Reason);

        return grant.Id;
    }
}
