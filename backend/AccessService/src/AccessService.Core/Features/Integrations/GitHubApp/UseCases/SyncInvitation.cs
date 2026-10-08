using AccessService.Contracts.Integrations.GitHub;
using AccessService.Core.Database;
using AccessService.Core.Diagnostics;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Integrations.GitHubApp.UseCases;

/// <summary>
///     Manual sync — юзер нажал «Я уже принял». Backend проверяет членство
///     через <c>GET /orgs/{org}/memberships/{username}</c>. Если active —
///     переводим invitation в ACCEPTED.
/// </summary>
public sealed class SyncGithubInvitationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/integrations/github/invitations/{id:guid}/sync/",
                async Task<EndpointResult<GithubInvitationStatusResponse>> (
                    [FromRoute] Guid id,
                    [FromServices] SyncGithubInvitationHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new SyncGithubInvitationCommand(id), ct))
            .RequireAuthorization()
            .RequireRateLimiting("github-app-invitation");
    }
}

public sealed record SyncGithubInvitationCommand(Guid Id) : ICommand;

public sealed class SyncGithubInvitationHandler
    : ICommandHandler<GithubInvitationStatusResponse, SyncGithubInvitationCommand>
{
    private readonly IGithubOrgInvitationsRepository _invitations;
    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly IGitHubAppApiClient _api;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly OnboardingMetrics _metrics;

    public SyncGithubInvitationHandler(
        IGithubOrgInvitationsRepository invitations,
        IAuthorGithubInstallationsRepository installations,
        IPlansRepository plans,
        ITransactionManager transactions,
        IGitHubAppApiClient api,
        UserScopedData user,
        TimeProvider time,
        OnboardingMetrics metrics)
    {
        _invitations = invitations;
        _installations = installations;
        _plans = plans;
        _transactions = transactions;
        _api = api;
        _user = user;
        _time = time;
        _metrics = metrics;
    }

    public async Task<Result<GithubInvitationStatusResponse, Error>> Handle(
        SyncGithubInvitationCommand cmd, CancellationToken ct)
    {
        GithubOrgInvitation? invitation = await _invitations.GetByIdAsync(cmd.Id, ct);
        if (invitation is null) return GitHubAppErrors.InvitationNotFound();
        if (invitation.UserId != _user.UserId) return AccessErrors.AccessDenied();

        if (invitation.Status == GithubInvitationStatus.ACCEPTED)
        {
            return CreateGithubInvitationHandler.Map(invitation);
        }

        Result<AccessService.Domain.Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == invitation.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;

        AuthorGithubInstallation? installation = await _installations.GetByAuthorAsync(planResult.Value.AuthorId, ct);
        if (installation is null || !installation.IsActive)
        {
            return GitHubAppErrors.InstallationNotFound();
        }

        Result<bool, Error> memberResult = await _api.IsOrgMemberAsync(
            installation.InstallationId, invitation.OrgLogin, invitation.GithubLogin, ct);
        if (memberResult.IsFailure) return memberResult.Error;

        DateTimeOffset now = _time.GetUtcNow();
        if (memberResult.Value)
        {
            invitation.MarkAccepted(now);
            _metrics.RecordInvitationOutcome("sync", "accepted");
        }
        else
        {
            invitation.MarkSynced(now);
            _metrics.RecordInvitationOutcome("sync", "not_member");
        }

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }
        return CreateGithubInvitationHandler.Map(invitation);
    }
}
