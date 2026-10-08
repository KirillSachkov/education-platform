using AccessService.Contracts.Integrations.GitHub;
using AccessService.Core.Database;
using AccessService.Domain.Integrations.GitHub;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Integrations.GitHubApp.UseCases;

public sealed class GetGithubInstallationStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/integrations/github/installation-status/",
                async Task<EndpointResult<GithubInstallationStatusResponse>> (
                    [FromServices] GetGithubInstallationStatusHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetGithubInstallationStatusQuery(), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record GetGithubInstallationStatusQuery : ICommand;

public sealed class GetGithubInstallationStatusHandler
    : ICommandHandler<GithubInstallationStatusResponse, GetGithubInstallationStatusQuery>
{
    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly UserScopedData _user;

    public GetGithubInstallationStatusHandler(
        IAuthorGithubInstallationsRepository installations,
        UserScopedData user)
    {
        _installations = installations;
        _user = user;
    }

    public async Task<Result<GithubInstallationStatusResponse, Error>> Handle(
        GetGithubInstallationStatusQuery _, CancellationToken ct)
    {
        AuthorGithubInstallation? installation = await _installations.GetByAuthorAsync(_user.UserId, ct);
        if (installation is null)
        {
            return new GithubInstallationStatusResponse(
                IsInstalled: false,
                OrgLogin: null,
                IsSuspended: false,
                InstalledAt: null);
        }

        return new GithubInstallationStatusResponse(
            IsInstalled: true,
            OrgLogin: installation.OrgLogin,
            IsSuspended: !installation.IsActive,
            InstalledAt: installation.InstalledAt);
    }
}
