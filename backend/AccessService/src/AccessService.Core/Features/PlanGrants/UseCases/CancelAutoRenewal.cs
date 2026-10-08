using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.PlanGrants.UseCases;

public sealed class CancelAutoRenewalEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/me/grants/{grantId:guid}/cancel-renewal/",
                async Task<EndpointResult<PlanGrantDto>> (
                    [FromRoute] Guid grantId,
                    [FromServices] CancelAutoRenewalHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new CancelAutoRenewalCommand(grantId), ct))
            .RequireAuthorization();
    }
}

public sealed record CancelAutoRenewalCommand(Guid GrantId) : ICommand;

public sealed class CancelAutoRenewalHandler
    : ICommandHandler<PlanGrantDto, CancelAutoRenewalCommand>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly IOrdersRepository _orders;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public CancelAutoRenewalHandler(
        IPlanGrantsRepository grants,
        IPlansRepository plans,
        IOrdersRepository orders,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time)
    {
        _grants = grants;
        _plans = plans;
        _orders = orders;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _time = time;
    }

    public async Task<Result<PlanGrantDto, Error>> Handle(
        CancelAutoRenewalCommand command,
        CancellationToken cancellationToken)
    {
        IAsyncDisposable? renewalLock = await _orders.TryAcquireRenewalLockAsync(
            command.GrantId,
            cancellationToken);
        if (renewalLock is null)
        {
            return Error.Conflict(
                "grant.renewal.lock_busy",
                "Операция автопродления уже выполняется. Повторите попытку позже");
        }

        await using (renewalLock)
        {
            Result<PlanGrant, Error> grantResult = await _grants.GetByAsync(
                g => g.Id == command.GrantId && g.UserId == _user.UserId,
                cancellationToken);
            if (grantResult.IsFailure)
            {
                return AccessErrors.GrantNotFound();
            }

            PlanGrant grant = grantResult.Value;
            Result<Plan, Error> planResult = await _plans.GetByAsync(
                p => p.Id == grant.PlanId,
                cancellationToken);
            if (planResult.IsFailure)
            {
                return AccessErrors.PlanNotFound();
            }

            Plan plan = planResult.Value;
            DateTimeOffset now = _time.GetUtcNow();
            bool wasCancelled = grant.AutoRenewalCancelledAt is not null;
            PlanGrantStatus previousStatus = grant.Status;
            int attempt = grant.ChargeFailureCount;

            UnitResult<Error> cancel = grant.CancelAutoRenewal(now);
            if (cancel.IsFailure)
            {
                return cancel.Error;
            }

            bool becameExpired = previousStatus == PlanGrantStatus.ACTIVE
                && grant.Status == PlanGrantStatus.EXPIRED;
            if (wasCancelled && !becameExpired)
            {
                return PlanGrantMapper.MapToDto(grant);
            }

            UnitResult<Error> begin = await _transactions.BeginTransactionAsync(cancellationToken);
            if (begin.IsFailure)
            {
                return begin.Error;
            }

            if (!wasCancelled)
            {
                Guid correlationId = Guid.CreateVersion7();
                await _outbox.PublishAsync(new PlanGrantRenewalCancelled(
                    grant.Id,
                    grant.UserId,
                    grant.PlanId,
                    plan.Tier.ToString(),
                    plan.AuthorId,
                    RenewalOrderId: null,
                    grant.AutoRenewalCancelledAt!.Value,
                    grant.AccessEndsAt!.Value,
                    attempt,
                    CorrelationId: correlationId));
            }

            if (becameExpired)
            {
                await _outbox.PublishAsync(new PlanGrantExpired(
                    grant.Id,
                    grant.UserId,
                    grant.PlanId,
                    plan.Tier.ToString(),
                    plan.AuthorId,
                    plan.FirstCourseId,
                    now,
                    [.. plan.Courses.Select(c => c.CourseId)],
                    plan.Id));
            }

            UnitResult<Error> commit = await _transactions.CommitTransactionAsync(cancellationToken);
            return commit.IsFailure ? commit.Error : PlanGrantMapper.MapToDto(grant);
        }
    }
}
