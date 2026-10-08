using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Vcs;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.GitHubApp;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace AssignmentReviewService.Core.Features.Installations.UseCases;

/// <summary>
///     Callback от GitHub после установки App. URL:
///     <c>GET /assignment-review/installations/callback?installation_id=...&amp;state=...&amp;setup_action=install</c>.
///     Anonymous endpoint — auth подменяется проверкой single-use state-token'а.
/// </summary>
public sealed class CompleteInstallationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/assignment-review/installations/callback/",
                async Task<Microsoft.AspNetCore.Http.IResult> (
                    [FromQuery(Name = "installation_id")] long installationId,
                    [FromQuery(Name = "state")] string? state,
                    [FromServices] CompleteInstallationHandler handler,
                    CancellationToken ct) =>
                {
                    Result<string, Error> result = await handler.Handle(installationId, state, ct);
                    if (result.IsFailure)
                    {
                        // Redirect back to /settings/integrations с error-кодом в query.
                        // 400 JSON показывал сырой ответ в браузере — bad UX.
                        string errorCode = Uri.EscapeDataString(result.Error.Messages[0].Code);
                        return Results.Redirect($"/settings/integrations?installation=error&code={errorCode}");
                    }

                    return Results.Redirect(result.Value);
                })
            .AllowAnonymousEndpoint()
            // Anonymous endpoint защищён single-use state-token'ом + 10-минутным TTL,
            // но per-IP burst-guard всё равно полезен на случай replay'я или scan'а.
            // Re-use "ar-github-webhook" policy (100 / min per IP) — semantically
            // тот же class of traffic: external callback, no auth, IP-scoped.
            .RequireRateLimiting("ar-github-webhook");
    }
}

public sealed class CompleteInstallationHandler
{
    private const string DEFAULT_RETURN_URL = "/settings/integrations?installation=success";

    private readonly IInstallStateStore<InstallStateData> _state;
    private readonly IVcsProvider _vcsProvider;
    private readonly IVcsInstallationsRepository _installations;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly ILogger<CompleteInstallationHandler> _logger;

    public CompleteInstallationHandler(
        IInstallStateStore<InstallStateData> state,
        IVcsProvider vcsProvider,
        IVcsInstallationsRepository installations,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time,
        ILogger<CompleteInstallationHandler> logger)
    {
        _state = state;
        _vcsProvider = vcsProvider;
        _installations = installations;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<string, Error>> Handle(long installationId, string? stateToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(stateToken))
        {
            return Error.Validation(
                "vcs.install_state.invalid",
                "Отсутствует state-token. Установка не может быть подтверждена.");
        }

        InstallStateData? state = await _state.ConsumeAsync(stateToken);
        if (state is null)
        {
            return Error.Validation(
                "vcs.install_state.invalid",
                "State-token недействителен или истёк. Запустите установку заново.");
        }

        // 1.5 hardening (#264): session-binding. State-token уже хранит UserId
        // (issuer'а из StartInstallation). Если caller аутентифицирован — он
        // ДОЛЖЕН совпадать с issuer'ом, иначе это CSRF-style hijack (атакующий
        // подсовывает свой state-token жертве). Anonymous caller'а пропускаем —
        // GitHub-redirect мог сбросить cookie, и блокировать legit-flow только
        // ради soft-hardening нельзя; state-token сам по себе single-use + 10min TTL.
        if (_user.IsAuthenticated && _user.UserId != state.UserId)
        {
            _logger.LogWarning(
                "GitHub install callback session mismatch: token issued for {IssuerUserId}, " +
                "but caller authenticated as {CallerUserId} (installation_id={InstallationId}).",
                state.UserId,
                _user.UserId,
                installationId);
            Error error = Error.Validation(
                "vcs.install_state.session_mismatch",
                "Установка GitHub App не может быть подтверждена из другой сессии. " +
                "Запустите установку заново под своей учётной записью.");
            await RestoreConsumedStateAsync(stateToken, state);
            return error;
        }

        // Fetch installation detail из GitHub: owner info + repo whitelist.
        Result<VcsInstallationDetail, Error> detailResult =
            await _vcsProvider.GetInstallationDetailAsync(installationId, ct);
        if (detailResult.IsFailure)
        {
            await RestoreConsumedStateAsync(stateToken, state);
            return detailResult.Error;
        }
        VcsInstallationDetail detail = detailResult.Value;

        string installationIdStr = installationId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        VcsInstallation? existing = await _installations.GetByAsync(
            i => i.Provider == VcsProvider.GITHUB && i.InstallationId == installationIdStr,
            ct);

        if (existing is null)
        {
            VcsInstallation created = VcsInstallation.Create(
                VcsProvider.GITHUB,
                installationIdStr,
                detail.OwnerType,
                detail.OwnerLogin,
                detail.OwnerExternalId,
                state.UserId,
                detail.RepoSelections);
            await _installations.AddAsync(created, ct);
        }
        else
        {
            // Re-install case: тот же InstallationId, обновляем metadata + linked user.
            existing.LinkUser(state.UserId);
            existing.UpdateOwnerMetadata(detail.OwnerLogin, detail.OwnerExternalId, detail.OwnerType);
            existing.UpdateRepoSelections(detail.RepoSelections);
            existing.Reactivate();
        }

        // Issue #307: publish vcs_installation.created so AccessService can
        // auto-complete the GITHUB_REVIEW_APP onboarding step. Buffered into the
        // outbox and flushed atomically with the entity write in SaveChanges below.
        await _outbox.PublishAsync(new VcsInstallationCreated(
            UserId: state.UserId,
            InstallationId: installationId,
            OwnerLogin: detail.OwnerLogin,
            OwnerType: detail.OwnerType.ToString(),
            OccurredAt: _time.GetUtcNow()));

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            await RestoreConsumedStateAsync(stateToken, state);
            return saveResult.Error;
        }

        _logger.LogInformation(
            "GitHub App installed by user={UserId} on {OwnerType}={OwnerLogin} (installation_id={InstallationId})",
            state.UserId,
            detail.OwnerType,
            detail.OwnerLogin,
            installationId);

        // Open-redirect защита: ReturnUrl мы валидировали при выпуске state в StartInstallationValidator.
        return string.IsNullOrEmpty(state.ReturnUrl) ? DEFAULT_RETURN_URL : state.ReturnUrl;
    }

    private Task RestoreConsumedStateAsync(string stateToken, InstallStateData state)
    {
        // Consume is atomic GETDEL. Restore on a rejected/transiently failed callback so a
        // mismatched session or temporary GitHub/DB outage cannot burn the victim's token.
        // A successful save still leaves the token consumed and preserves single-use semantics.
        return _state.SetAsync(stateToken, state, StartInstallationHandler.STATE_TTL);
    }
}
