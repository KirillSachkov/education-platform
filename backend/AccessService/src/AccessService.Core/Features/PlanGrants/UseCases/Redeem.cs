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

public sealed class RedeemInviteEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/invites/{token}/redeem", async Task<EndpointResult<PlanGrantDto>> (
                [FromRoute] string token,
                [FromServices] RedeemInviteHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new RedeemInviteCommand(token), ct))
            .RequireAuthorization()
            .RequireRateLimiting("invite-redeem");
    }
}

public sealed record RedeemInviteCommand(string Token) : ICommand;

public sealed class RedeemInviteHandler : ICommandHandler<PlanGrantDto, RedeemInviteCommand>
{
    private readonly IInviteLinksRepository _invites;
    private readonly IPlansRepository _plans;
    private readonly IPlanGrantsRepository _grants;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<RedeemInviteHandler> _logger;

    public RedeemInviteHandler(
        IInviteLinksRepository invites,
        IPlansRepository plans,
        IPlanGrantsRepository grants,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<RedeemInviteHandler> logger)
    {
        _invites = invites;
        _plans = plans;
        _grants = grants;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<PlanGrantDto, Error>> Handle(
        RedeemInviteCommand command,
        CancellationToken cancellationToken)
    {
        Result<InviteToken, Error> tokenVo = InviteToken.Of(command.Token);
        if (tokenVo.IsFailure)
        {
            return AccessErrors.InviteNotFound();
        }

        string tokenValue = tokenVo.Value.Value;
        Result<InviteLink, Error> getInvite = await _invites.GetByAsync(
            i => i.Token.Value == tokenValue, cancellationToken);
        if (getInvite.IsFailure)
        {
            return getInvite.Error;
        }

        InviteLink invite = getInvite.Value;

        UnitResult<Error> validate = invite.ValidateForRedeem(DateTimeOffset.UtcNow);
        if (validate.IsFailure)
        {
            return validate.Error;
        }

        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == invite.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return AccessErrors.PlanNotFound();
        }

        Plan plan = getPlan.Value;
        if (plan.ArchivedAt is not null)
        {
            return AccessErrors.PlanArchived();
        }

        // Idempotency: same user + same invite + ACTIVE → return existing grant.
        Guid userId = _user.UserId;
        Guid inviteId = invite.Id;
        Result<PlanGrant, Error> existing = await _grants.GetByAsync(
            g => g.UserId == userId
                 && g.PlanId == plan.Id
                 && g.Source == PlanGrantSource.INVITE_LINK
                 && g.SourceRef == inviteId
                 && g.Status == PlanGrantStatus.ACTIVE,
            cancellationToken);
        if (existing.IsSuccess)
        {
            return PlanGrantMapper.MapToDto(existing.Value);
        }

        PlanGrant grant = PlanGrant.Create(
            userId,
            plan.Id,
            PlanGrantSource.INVITE_LINK,
            invite.Id);

        await _grants.AddAsync(grant, cancellationToken);
        invite.RegisterUsage();

        InviteRedemption redemption = new(invite.Id, userId, grant.Id, ipHash: null);
        await _grants.AddRedemptionAsync(redemption, cancellationToken);

        await _outbox.PublishAsync(new PlanGrantCreated(
            grant.Id,
            grant.UserId,
            plan.Id,
            plan.Tier.ToString(),
            plan.AuthorId,
            plan.FirstCourseId,
            plan.IncludesFutureContent,
            grant.Source.ToString(),
            grant.SourceRef,
            grant.GrantedAt,
            grant.ExpiresAt,
            PlanCapabilitiesMapper.ToStrings(plan.Capabilities),
            [.. plan.Courses.Select(c => c.CourseId)],
            plan.DisplayName.Value,
            plan.OfferType.ToString()));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            return save.Error;
        }

        _logger.LogInformation(
            "Plan grant {GrantId} created for user {UserId} via invite {InviteId} on plan {PlanId}",
            grant.Id, userId, invite.Id, plan.Id);

        return PlanGrantMapper.MapToDto(grant);
    }
}
