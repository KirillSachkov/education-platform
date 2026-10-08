using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.PlanGrants.UseCases;

public sealed class GrantTrialCreditOverrideEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/admin/users/{userId:guid}/trial-credit-override/", async Task<EndpointResult<PlanGrantDto>> (
                [FromRoute] Guid userId,
                [FromBody] TrialCreditOverrideRequest request,
                [FromServices] GrantTrialCreditOverrideHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GrantTrialCreditOverrideCommand(userId, request), ct))
            .RequirePermissions(PlatformPermissions.Plans.GRANT);
    }
}

public sealed record GrantTrialCreditOverrideCommand(Guid UserId, TrialCreditOverrideRequest Request) : ICommand;

public sealed class GrantTrialCreditOverrideHandler : ICommandHandler<PlanGrantDto, GrantTrialCreditOverrideCommand>
{
    /// <summary>
    /// Legacy default для admin-override (#580), если <c>until</c> в запросе не задан.
    /// Paid trial credit больше не сгорает; endpoint оставлен для совместимости.
    /// </summary>
    private const int DefaultCreditOverrideDays = 30;

    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<GrantTrialCreditOverrideHandler> _logger;

    public GrantTrialCreditOverrideHandler(
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<GrantTrialCreditOverrideHandler> logger)
    {
        _plans = plans;
        _grants = grants;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<PlanGrantDto, Error>> Handle(
        GrantTrialCreditOverrideCommand command,
        CancellationToken cancellationToken)
    {
        TrialCreditOverrideRequest request = command.Request;

        // Past until — бессмысленный legacy override, отвергаем явно для ясности (#580).
        if (request.Until is { } requestedUntil && requestedUntil <= DateTimeOffset.UtcNow)
        {
            return AccessErrors.TrialOverrideUntilInPast();
        }

        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == request.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return getPlan.Error;
        }

        Plan plan = getPlan.Value;

        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        if (!plan.IsTrial)
        {
            return AccessErrors.TrialTierInvalid();
        }

        Result<PlanGrant, Error> getGrant = await _grants.GetByAsync(
            g => g.UserId == command.UserId
                 && g.PlanId == plan.Id
                 && (g.Status == PlanGrantStatus.ACTIVE || g.Status == PlanGrantStatus.EXPIRED),
            cancellationToken);
        if (getGrant.IsFailure)
        {
            return AccessErrors.GrantNotFound();
        }

        PlanGrant grant = getGrant.Value;

        DateTimeOffset until = request.Until ?? DateTimeOffset.UtcNow.AddDays(DefaultCreditOverrideDays);
        grant.SetCreditOverride(until);

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Trial credit override set on grant {GrantId} (user {UserId}, plan {PlanId}) until {Until:O} by {IssuerId}",
            grant.Id, command.UserId, plan.Id, until, _user.UserId);

        return PlanGrantMapper.MapToDto(grant);
    }
}
