using AccessService.Core.Database;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Shared.GitHubApp;
using GitHubAppErrors = AccessService.Domain.GitHubAppErrors;
using GitHubAppOptions = AccessService.Core.Features.Integrations.GitHubApp.GitHubAppOptions;

namespace AccessService.Core.Features.Integrations.GitHubApp.UseCases;

/// <summary>
///     Callback от GitHub после установки App в org. URL:
///     <c>GET /access/integrations/github/install-callback?installation_id=...&amp;state=...&amp;setup_action=install</c>.
///     Сверяет state, читает installation detail у GitHub API, сохраняет/обновляет
///     <see cref="AuthorGithubInstallation"/> и редиректит юзера обратно на frontend.
///     Anonymous endpoint — auth подменяется проверкой state token'а.
/// </summary>
public sealed class InstallCallbackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/integrations/github/install-callback/",
                async Task<Microsoft.AspNetCore.Http.IResult> (
                    [FromQuery(Name = "installation_id")] long installationId,
                    [FromQuery(Name = "state")] string? state,
                    [FromServices] InstallCallbackHandler handler,
                    CancellationToken ct) =>
                {
                    Result<string, Error> result = await handler.Handle(installationId, state, ct);
                    if (result.IsFailure)
                    {
                        return Results.BadRequest(new
                        {
                            code = result.Error.Messages[0].Code,
                            message = result.Error.Messages[0].Message,
                        });
                    }

                    return Results.Redirect(result.Value);
                })
            .AllowAnonymous()
            .RequireRateLimiting("github-app-install");
    }
}

public sealed class InstallCallbackHandler
{
    private readonly IInstallStateStore<InstallStateData> _state;
    private readonly IGitHubAppApiClient _api;
    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly ITransactionManager _transactions;
    private readonly TimeProvider _time;
    private readonly GitHubAppOptions _options;
    private readonly ILogger<InstallCallbackHandler> _logger;

    public InstallCallbackHandler(
        IInstallStateStore<InstallStateData> state,
        IGitHubAppApiClient api,
        IAuthorGithubInstallationsRepository installations,
        ITransactionManager transactions,
        TimeProvider time,
        IOptions<GitHubAppOptions> options,
        ILogger<InstallCallbackHandler> logger)
    {
        _state = state;
        _api = api;
        _installations = installations;
        _transactions = transactions;
        _time = time;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Result<string, Error>> Handle(long installationId, string? stateToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stateToken))
        {
            return GitHubAppErrors.InstallStateInvalid();
        }

        InstallStateData? state = await _state.ConsumeAsync(stateToken);
        if (state is null)
        {
            return GitHubAppErrors.InstallStateInvalid();
        }

        Result<InstallationDetail, Error> detailResult = await _api.GetInstallationAsync(installationId, ct);
        if (detailResult.IsFailure) return detailResult.Error;

        DateTimeOffset now = _time.GetUtcNow();
        AuthorGithubInstallation? existing = await _installations.GetByAuthorAsync(state.AuthorId, ct);
        if (existing is null)
        {
            existing = AuthorGithubInstallation.Create(
                state.AuthorId, installationId, detailResult.Value.AccountLogin, now);
            await _installations.AddAsync(existing, ct);
        }
        else
        {
            existing.Reinstall(installationId, detailResult.Value.AccountLogin, now);
        }

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "GitHub App installed by author={AuthorId} on org={OrgLogin} (installation_id={InstallationId})",
            state.AuthorId, detailResult.Value.AccountLogin, installationId);

        // Return URL: если в state был planId — редиректим на edit-страницу плана.
        return state.PlanId.HasValue
            ? $"/author/plans/{state.PlanId.Value:D}/edit?github=connected"
            : _options.FrontendInstallReturnUrl;
    }
}
