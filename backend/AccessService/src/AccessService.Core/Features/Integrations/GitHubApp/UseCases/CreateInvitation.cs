using AccessService.Contracts.Integrations.GitHub;
using AccessService.Core.Database;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Integrations.GitHubApp.UseCases;

public sealed class CreateGithubInvitationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/integrations/github/invitations/",
                async Task<EndpointResult<GithubInvitationStatusResponse>> (
                    [FromBody] CreateGithubInvitationRequest request,
                    [FromServices] CreateGithubInvitationHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new CreateGithubInvitationCommand(request), ct))
            .RequireAuthorization()
            .RequireRateLimiting("github-app-invitation");
    }
}

public sealed record CreateGithubInvitationCommand(CreateGithubInvitationRequest Request) : ICommand;

public sealed class CreateGithubInvitationHandler
    : ICommandHandler<GithubInvitationStatusResponse, CreateGithubInvitationCommand>
{
    private readonly IPlansRepository _plans;
    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly IGithubOrgInvitationsRepository _invitations;
    private readonly IPlanGrantsRepository _grants;
    private readonly IAuthServiceClient _auth;
    private readonly ITransactionManager _transactions;
    private readonly IGitHubAppApiClient _api;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;
    private readonly ILogger<CreateGithubInvitationHandler> _logger;

    public CreateGithubInvitationHandler(
        IPlansRepository plans,
        IAuthorGithubInstallationsRepository installations,
        IGithubOrgInvitationsRepository invitations,
        IPlanGrantsRepository grants,
        IAuthServiceClient auth,
        ITransactionManager transactions,
        IGitHubAppApiClient api,
        UserScopedData user,
        TimeProvider time,
        ILogger<CreateGithubInvitationHandler> logger)
    {
        _plans = plans;
        _installations = installations;
        _invitations = invitations;
        _grants = grants;
        _auth = auth;
        _transactions = transactions;
        _api = api;
        _user = user;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<GithubInvitationStatusResponse, Error>> Handle(
        CreateGithubInvitationCommand cmd, CancellationToken ct)
    {
        CreateGithubInvitationRequest req = cmd.Request;
        if (string.IsNullOrWhiteSpace(req.GithubLogin))
        {
            return GitHubAppErrors.UserGithubLoginMissing();
        }

        Result<AccessService.Domain.Plan, Error> planResult = await _plans.GetByAsync(p => p.Id == req.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;
        AccessService.Domain.Plan plan = planResult.Value;
        if (!plan.IsActive || plan.ArchivedAt is not null)
        {
            return AccessErrors.AccessDenied();
        }
        if (string.IsNullOrEmpty(plan.GitHubOrg))
        {
            return GitHubAppErrors.PlanGithubOrgMissing();
        }

        DateTimeOffset now = _time.GetUtcNow();
        IReadOnlyList<PlanGrant> activeGrants = await _grants.GetManyByAsync(
            grant => grant.UserId == _user.UserId
                     && grant.Status == PlanGrantStatus.ACTIVE
                     && (grant.ExpiresAt == null || grant.ExpiresAt > now),
            ct);
        Guid[] grantPlanIds = activeGrants.Select(g => g.PlanId).Distinct().ToArray();
        IReadOnlyList<AccessService.Domain.Plan> grantPlans = grantPlanIds.Length == 0
            ? []
            : await _plans.GetManyByAsync(
                p => grantPlanIds.Contains(p.Id) && p.IsActive && p.ArchivedAt == null,
                ct);
        Dictionary<Guid, AccessService.Domain.Plan> grantPlansById = grantPlans.ToDictionary(p => p.Id);
        if (!GrantScopeGuard.IsAlreadyCovered(plan, activeGrants, grantPlansById))
        {
            return AccessErrors.AccessDenied();
        }

        Result<UserGithubLoginResponse, Error> authUser =
            await _auth.GetUserGithubLoginAsync(_user.UserId, ct);
        string? linkedGithubLogin = authUser.IsSuccess ? authUser.Value.GithubLogin : null;
        if (string.IsNullOrWhiteSpace(linkedGithubLogin))
        {
            return GitHubAppErrors.UserGithubLoginMissing();
        }
        if (!string.Equals(linkedGithubLogin, req.GithubLogin.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return AccessErrors.AccessDenied();
        }

        // Идемпотентно только для «живых» статусов: PENDING (приглашение в пути) и
        // ACCEPTED (членство подтверждено). Терминально-неуспешные строки
        // (FAILED/EXPIRED/CANCELED) НЕ замораживаем навсегда (#501): условия меняются
        // (автор установил App, юзера добавили в org вручную, GitHub-аккаунт сменился) —
        // пере-прогоняем флоу и ОБНОВЛЯЕМ ту же строку. До фикса 17 прод-юзеров
        // навечно застряли на FAILED(no_installation) из эпохи до установки App.
        GithubOrgInvitation? existing = await _invitations.GetByUserPlanAsync(_user.UserId, req.PlanId, ct);
        if (existing is { Status: GithubInvitationStatus.PENDING or GithubInvitationStatus.ACCEPTED })
        {
            return Map(existing);
        }

        AuthorGithubInstallation? installation = await _installations.GetByAuthorAsync(plan.AuthorId, ct);
        if (installation is null || !installation.IsActive)
        {
            // Fallback: автор ещё не подключил App или App suspended.
            return await UpsertFailedAsync(existing, req, plan.GitHubOrg!, "no_installation", now, ct);
        }

        // 1) (#501) Сначала live-проверка членства: юзер мог вступить в org сам или быть
        // добавлен автором вручную — тогда приглашение не нужно, фиксируем ACCEPTED сразу.
        // Ошибка проверки не блокирует — продолжаем обычный invite-флоу.
        Result<bool, Error> memberResult = await _api.IsOrgMemberAsync(
            installation.InstallationId, plan.GitHubOrg!, req.GithubLogin, ct);
        if (memberResult is { IsSuccess: true, Value: true })
        {
            GithubOrgInvitation accepted;
            if (existing is null)
            {
                accepted = GithubOrgInvitation.CreateAlreadyMember(
                    req.PlanId, _user.UserId, req.GithubLogin, plan.GitHubOrg!, now);
                await _invitations.AddAsync(accepted, ct);
            }
            else
            {
                existing.MarkAccepted(now);
                accepted = existing;
            }

            UnitResult<Error> saveAccepted = await _transactions.SaveChangesAsync(ct);
            if (saveAccepted.IsFailure)
            {
                return saveAccepted.Error;
            }

            _logger.LogInformation(
                "GitHub invitation short-circuited to ACCEPTED — user already org member (plan={PlanId}, user={UserId})",
                req.PlanId, _user.UserId);
            return Map(accepted);
        }

        // 2) Достаём numeric GitHub user id (нужен для API invite).
        Result<long, Error> userIdResult = await _api.GetUserIdByLoginAsync(
            installation.InstallationId, req.GithubLogin, ct);
        if (userIdResult.IsFailure || userIdResult.Value == 0L)
        {
            return await UpsertFailedAsync(existing, req, plan.GitHubOrg!, "github_user_not_found", now, ct);
        }

        // 3) Создаём invitation (upsert: re-attempt обновляет существующую строку).
        CreateInvitationResult apiResult = await _api.CreateOrgInvitationAsync(
            installation.InstallationId, plan.GitHubOrg!, userIdResult.Value, ct);

        switch (apiResult)
        {
            case CreateInvitationResult.Created c when existing is not null:
                existing.MarkPendingAgain(c.InvitationId, now);
                break;
            case CreateInvitationResult.Created c:
                existing = GithubOrgInvitation.CreatePending(req.PlanId, _user.UserId,
                    req.GithubLogin, plan.GitHubOrg!, c.InvitationId, now);
                await _invitations.AddAsync(existing, ct);
                break;
            case CreateInvitationResult.AlreadyMember when existing is not null:
                existing.MarkAccepted(now);
                break;
            case CreateInvitationResult.AlreadyMember:
                existing = GithubOrgInvitation.CreateAlreadyMember(req.PlanId, _user.UserId,
                    req.GithubLogin, plan.GitHubOrg!, now);
                await _invitations.AddAsync(existing, ct);
                break;
            case CreateInvitationResult.UserNotFound:
                return await UpsertFailedAsync(existing, req, plan.GitHubOrg!, "github_user_not_found", now, ct);
            case CreateInvitationResult.TokenInvalid:
                return await UpsertFailedAsync(existing, req, plan.GitHubOrg!, "token_invalid", now, ct);
            case CreateInvitationResult.UnknownFailure u:
                return await UpsertFailedAsync(existing, req, plan.GitHubOrg!, u.Reason, now, ct);
            default:
                return await UpsertFailedAsync(existing, req, plan.GitHubOrg!, "unknown", now, ct);
        }

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "GitHub invitation created (plan={PlanId}, user={UserId}, status={Status})",
            req.PlanId, _user.UserId, existing.Status);

        return Map(existing);
    }

    /// <summary>
    ///     Фиксирует неуспех попытки: обновляет существующую строку (re-attempt, #501)
    ///     либо создаёт новую FAILED-запись.
    /// </summary>
    private async Task<Result<GithubInvitationStatusResponse, Error>> UpsertFailedAsync(
        GithubOrgInvitation? existing,
        CreateGithubInvitationRequest req,
        string orgLogin,
        string failureReason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        GithubOrgInvitation failed;
        if (existing is null)
        {
            failed = GithubOrgInvitation.CreateFailed(
                req.PlanId, _user.UserId, req.GithubLogin, orgLogin, failureReason, now);
            await _invitations.AddAsync(failed, ct);
        }
        else
        {
            existing.MarkFailedAgain(failureReason, now);
            failed = existing;
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
        {
            return save.Error;
        }
        return Map(failed);
    }

    internal static GithubInvitationStatusResponse Map(GithubOrgInvitation i) =>
        new(
            i.Id,
            i.Status.ToString(),
            i.OrgLogin,
            i.GithubLogin,
            i.FailureReason,
            i.CreatedAt,
            i.AcceptedAt,
            i.LastSyncedAt);
}
