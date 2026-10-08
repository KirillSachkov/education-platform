using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.InviteLinks.UseCases;

public sealed class GetInvitePreviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/invites/{token}/preview", async Task<EndpointResult<InvitePreviewDto>> (
                [FromRoute] string token,
                [FromServices] GetInvitePreviewHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetInvitePreviewQuery(token), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed record GetInvitePreviewQuery(string Token) : IQuery;

public sealed class GetInvitePreviewHandler
    : IQueryHandlerWithResult<InvitePreviewDto, GetInvitePreviewQuery>
{
    private readonly IInviteLinksRepository _invites;
    private readonly IPlansRepository _plans;

    public GetInvitePreviewHandler(IInviteLinksRepository invites, IPlansRepository plans)
    {
        _invites = invites;
        _plans = plans;
    }

    public async Task<Result<InvitePreviewDto, Error>> Handle(
        GetInvitePreviewQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<InviteToken, Error> tokenVo = InviteToken.Of(query.Token);
        if (tokenVo.IsFailure)
        {
            // Don't leak format details — return the same not-found error.
            return AccessErrors.InviteNotFound();
        }

        // EF gotcha: owned-VO equality doesn't translate; pull primitive into local.
        string tokenValue = tokenVo.Value.Value;

        Result<InviteLink, Error> getInvite = await _invites.GetByAsync(
            i => i.Token.Value == tokenValue, cancellationToken);
        if (getInvite.IsFailure)
        {
            return getInvite.Error;
        }

        InviteLink invite = getInvite.Value;

        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == invite.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return AccessErrors.PlanNotFound();
        }

        Plan plan = getPlan.Value;

        UnitResult<Error> validate = invite.ValidateForRedeem(DateTimeOffset.UtcNow);
        bool available = validate.IsSuccess;
        string? reason = validate.IsFailure ? validate.Error.Messages[0].Code : null;

        return new InvitePreviewDto(
            plan.Tier.ToString(),
            plan.DisplayName.Value,
            plan.ShortDescription,
            plan.CoverFileId,
            plan.Features,
            plan.GetCourseIds(),
            plan.IncludesFutureContent,
            available,
            reason,
            plan.AuthorId);
    }
}
