using AssignmentReviewService.Contracts.Installations;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Vcs;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AssignmentReviewService.Core.Features.Admin.UseCases;

/// <summary>
///     <c>GET /assignment-review/admin/users/{userId}/installations/</c> (#444) — admin/support
///     variant of <see cref="Installations.UseCases.GetMyInstallationsHandler"/>: GitHub App
///     installations linked to an ARBITRARY user (id from route, not the caller). Feeds the
///     «GitHub/App status» block of the post-purchase support panel — lets support see whether the
///     buyer connected the AI-review GitHub App and on which account/org. Read-only.
///     <para>
///         Gated by <c>ADMIN</c>/<c>MODERATOR</c> (not the <c>Platform.ADMIN</c>-only ai-settings
///         endpoints): the post-purchase panel is a support tool moderators use, matching the
///         AccessService post-purchase-status / TelegramBotService admin reads it sits next to.
///     </para>
/// </summary>
public sealed record GetUserInstallationsQuery(Guid UserId) : IQuery;

public sealed class GetUserInstallationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/assignment-review/admin/users/{userId:guid}/installations/",
                async Task<EndpointResult<GetMyInstallationsResponse>> (
                    [FromRoute] Guid userId,
                    [FromServices] GetUserInstallationsHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetUserInstallationsQuery(userId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
    }
}

public sealed class GetUserInstallationsHandler
    : IQueryHandlerWithResult<GetMyInstallationsResponse, GetUserInstallationsQuery>
{
    private readonly IVcsInstallationsRepository _installations;

    public GetUserInstallationsHandler(IVcsInstallationsRepository installations)
    {
        _installations = installations;
    }

    public async Task<Result<GetMyInstallationsResponse, Error>> Handle(
        GetUserInstallationsQuery query, CancellationToken ct)
    {
        IReadOnlyList<VcsInstallation> installations =
            await _installations.GetManyByAsync(i => i.LinkedUserId == query.UserId, ct);

        IReadOnlyList<VcsInstallationDto> dtos = installations
            .Select(i => new VcsInstallationDto
            {
                Id = i.Id,
                Provider = i.Provider.ToString(),
                OwnerLogin = i.OwnerLogin,
                OwnerType = i.OwnerType.ToString(),
                Status = i.Status.ToString(),
                AllRepos = i.RepoSelections.All,
                Repos = i.RepoSelections.Repos,
                InstalledAt = i.InstalledAt,
                RemovedAt = i.RemovedAt,
            })
            .ToList();

        return new GetMyInstallationsResponse { Installations = dtos };
    }
}
