using System.Security.Cryptography;
using AccessService.Contracts.Integrations.GitHub;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.GitHubApp;
using GitHubAppErrors = AccessService.Domain.GitHubAppErrors;
using GitHubAppOptions = AccessService.Core.Features.Integrations.GitHubApp.GitHubAppOptions;

namespace AccessService.Core.Features.Integrations.GitHubApp.UseCases;

public sealed class InstallRedirectEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/integrations/github/install-redirect/",
                async Task<EndpointResult<InstallRedirectResponse>> (
                    [FromBody] InstallRedirectRequest request,
                    [FromServices] InstallRedirectHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new InstallRedirectCommand(request.PlanId), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE)
            .RequireRateLimiting("github-app-install");
    }
}

public sealed record InstallRedirectCommand(Guid? PlanId) : ICommand;

public sealed class InstallRedirectHandler : ICommandHandler<InstallRedirectResponse, InstallRedirectCommand>
{
    private static readonly TimeSpan STATE_TTL = TimeSpan.FromMinutes(10);

    private readonly IInstallStateStore<InstallStateData> _state;
    private readonly UserScopedData _user;
    private readonly GitHubAppOptions _options;

    public InstallRedirectHandler(
        IInstallStateStore<InstallStateData> state,
        UserScopedData user,
        IOptions<GitHubAppOptions> options)
    {
        _state = state;
        _user = user;
        _options = options.Value;
    }

    public async Task<Result<InstallRedirectResponse, Error>> Handle(
        InstallRedirectCommand command, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_options.Slug))
        {
            return GitHubAppErrors.AppApiCallFailed("GitHubApp.Slug not configured");
        }

        // 32 random bytes → URL-safe base64.
        byte[] randomBytes = RandomNumberGenerator.GetBytes(32);
        string token = Convert.ToBase64String(randomBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        await _state.SetAsync(token, new InstallStateData(_user.UserId, command.PlanId), STATE_TTL);

        string url = $"https://github.com/apps/{_options.Slug}/installations/new?state={token}";
        return new InstallRedirectResponse(url);
    }
}
