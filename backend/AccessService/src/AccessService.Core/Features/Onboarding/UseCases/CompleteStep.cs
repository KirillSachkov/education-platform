using AccessService.Core.Database;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AccessService.Domain.Onboarding;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Middleware;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.Core.Features.Onboarding.UseCases;

public sealed class CompleteOnboardingStepEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/onboarding/{planId:guid}/steps/{stepId:guid}/complete/",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid planId,
                    [FromRoute] Guid stepId,
                    [FromServices] CompleteOnboardingStepHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new CompleteOnboardingStepCommand(planId, stepId), ct))
            .RequireAuthorization();
    }
}

public sealed record CompleteOnboardingStepCommand(Guid PlanId, Guid StepId) : ICommand;

/// <summary>
///     Завершение шага онбординга. Для проверяемых шагов выполняется server-side верификация
///     факта выполнения ДО завершения (#444): TELEGRAM — членство в группе через
///     TelegramBotService; GITHUB — принятое приглашение в org. GITHUB_REVIEW_APP завершается
///     авто-consumer'ом <c>vcs_installation.created</c> (установку App из AccessService не
///     проверить — она живёт в AssignmentReviewService, поэтому ручной complete остаётся
///     fallback'ом, чтобы не застрял вернувшийся юзер с уже установленным App без нового
///     install-события). MARKDOWN / NOTIFICATIONS не проверяются. Пропускаемые шаги можно пропустить
///     (<see cref="SkipOnboardingStepHandler"/>) — это escape hatch, когда пользователь пока не
///     готов выполнить шаг. До этой проверки кнопка «Далее» завершала любой шаг без подтверждения
///     факта вступления/привязки.
/// </summary>
public sealed class CompleteOnboardingStepHandler : ICommandHandler<Guid, CompleteOnboardingStepCommand>
{
    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly IGithubOrgInvitationsRepository _githubInvitations;
    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly IPlansRepository _plans;
    private readonly IAuthServiceClient _auth;
    private readonly ITelegramBotServiceClient _telegram;
    private readonly IGitHubAppApiClient _githubApi;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly ILogger<CompleteOnboardingStepHandler> _logger;

    public CompleteOnboardingStepHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        IGithubOrgInvitationsRepository githubInvitations,
        IAuthorGithubInstallationsRepository installations,
        IPlansRepository plans,
        IAuthServiceClient auth,
        ITelegramBotServiceClient telegram,
        IGitHubAppApiClient githubApi,
        ITransactionManager transactions,
        UserScopedData user,
        ILogger<CompleteOnboardingStepHandler> logger)
    {
        _onboardings = onboardings;
        _flows = flows;
        _githubInvitations = githubInvitations;
        _installations = installations;
        _plans = plans;
        _auth = auth;
        _telegram = telegram;
        _githubApi = githubApi;
        _transactions = transactions;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(CompleteOnboardingStepCommand cmd, CancellationToken ct)
    {
        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(_user.UserId, cmd.PlanId, ct);
        if (onboarding is null) return OnboardingErrors.OnboardingNotFound();

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            f => f.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        PlanOnboardingStep? step = flowResult.Value.Steps.FirstOrDefault(s => s.Id == cmd.StepId);
        if (step is null) return OnboardingErrors.StepNotFound();

        // Идемпотентность: уже завершён → не пере-верифицируем (членство может легитимно
        // измениться) и не «раз-завершаем» шаг, но ПРОДВИГАЕМ курсор. Иначе при курсоре,
        // припаркованном на уже завершённом шаге (back-nav по степперу / return-to или
        // пропущенный advance), «Далее» был no-op'ом — юзер залипал на пройденном шаге (#596).
        if (onboarding.CompletedStepIds.Contains(cmd.StepId))
        {
            onboarding.AdvanceTo(flowResult.Value.Steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id));
            UnitResult<Error> advanceSave = await _transactions.SaveChangesAsync(ct);
            if (advanceSave.IsFailure) return advanceSave.Error;
            return cmd.StepId;
        }

        // Server-side проверка факта выполнения для проверяемых шагов.
        UnitResult<Error> verified = await VerifyStepAsync(step.Type, cmd.PlanId, ct);
        if (verified.IsFailure) return verified.Error;

        UnitResult<Error> complete = onboarding.CompleteStep(cmd.StepId);
        if (complete.IsFailure) return complete.Error;

        onboarding.AdvanceTo(flowResult.Value.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;
        return cmd.StepId;
    }

    private async Task<UnitResult<Error>> VerifyStepAsync(
        PlanOnboardingStepType type,
        Guid planId,
        CancellationToken ct) =>
        type switch
        {
            PlanOnboardingStepType.TELEGRAM => await VerifyTelegramAsync(planId, ct),
            PlanOnboardingStepType.GITHUB => await VerifyGithubAsync(planId, ct),
            // GITHUB_REVIEW_APP: основной путь — авто-complete consumer'ом vcs_installation.created.
            // Жёсткий reject ручного complete застрял бы у вернувшегося юзера с уже установленным App
            // (нового install-события нет) на non-skippable шаге — поэтому не верифицируем.
            _ => UnitResult.Success<Error>(),
        };

    private async Task<UnitResult<Error>> VerifyTelegramAsync(Guid planId, CancellationToken ct)
    {
        try
        {
            Result<PlanMembershipDto, Error> membership =
                await _telegram.CheckPlanMembershipAsync(_user.UserId, planId, ct);
            if (membership.IsSuccess && membership.Value.IsMember)
                return UnitResult.Success<Error>();
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex,
                "CompleteStep TELEGRAM verification failed for user={UserId} plan={PlanId} — rejecting complete",
                _user.UserId, planId);
        }

        // not_member / unknown / сервис недоступен → не завершаем (членство не подтверждено).
        return OnboardingErrors.TelegramMembershipRequired();
    }

    private async Task<UnitResult<Error>> VerifyGithubAsync(Guid planId, CancellationToken ct)
    {
        // Путь invite-flow: юзер запросил приглашение и принял его → ACCEPTED-запись.
        GithubOrgInvitation? invitation =
            await _githubInvitations.GetByUserPlanAsync(_user.UserId, planId, ct);
        if (invitation is { Status: GithubInvitationStatus.ACCEPTED })
            return UnitResult.Success<Error>();

        // Путь «уже состоит в org»: юзер вступил в org напрямую / был добавлен вручную /
        // получил доступ через GITHUB_ORG-grant — invitation-записи у него НЕТ. Зелёная карточка
        // GitHub-шага показывает «вы состоите в org … можно дальше» по членству в org из AuthService
        // (`auth.user_github_orgs`). Сверяемся с ТЕМ ЖЕ источником, чтобы карточка и server-side
        // проверка «Далее» не расходились (#448). Здесь не нужен GitHub App у автора — в отличие
        // от live invitation-sync — поэтому подтверждённый член проходит даже без установленного App.
        if (await IsConfirmedOrgMemberAsync(planId, ct))
            return UnitResult.Success<Error>();

        // (#501) Третий fallback — live-проверка через GitHub App автора. Кэш orgs из
        // AuthService наполняется только при GitHub-OAuth-логине и протухает (вручную
        // добавленный в org юзер в него не попадает), а invitation-строка может висеть
        // FAILED из эпохи «App не установлен» или отсутствовать вовсе (#1148). Fail-closed:
        // без подтверждения шаг не завершается, но «не состоит» и «не смогли проверить»
        // возвращают разные ошибки, чтобы шаг показал верную подсказку.
        return await CheckLiveOrgMembershipAsync(planId, invitation, ct) switch
        {
            LiveMembership.Member => UnitResult.Success<Error>(),
            LiveMembership.Unavailable => OnboardingErrors.GithubVerificationUnavailable(),
            _ => OnboardingErrors.GithubMembershipRequired(),
        };
    }

    private enum LiveMembership { Member, NotMember, Unavailable }

    private async Task<LiveMembership> CheckLiveOrgMembershipAsync(
        Guid planId, GithubOrgInvitation? invitation, CancellationToken ct)
    {
        try
        {
            Result<AccessService.Domain.Plan, Error> plan = await _plans.GetByAsync(p => p.Id == planId, ct);
            if (plan.IsFailure || string.IsNullOrEmpty(plan.Value.GitHubOrg))
                return LiveMembership.NotMember;

            // Канонический логин — привязанный GitHub из AuthService (после перепривязки
            // invitation-строка хранит старый). Логин invitation-строки — запасной.
            Result<UserGithubLoginResponse, Error> linked =
                await _auth.GetUserGithubLoginAsync(_user.UserId, ct);
            string? githubLogin = linked.IsSuccess ? linked.Value.GithubLogin : null;
            if (string.IsNullOrWhiteSpace(githubLogin))
                githubLogin = invitation?.GithubLogin;
            if (string.IsNullOrWhiteSpace(githubLogin) && linked.IsFailure)
                return LiveMembership.Unavailable;

            // GitHub не привязан — шаг сам покажет «Привяжи GitHub».
            if (string.IsNullOrWhiteSpace(githubLogin))
                return LiveMembership.NotMember;

            AuthorGithubInstallation? installation =
                await _installations.GetByAuthorAsync(plan.Value.AuthorId, ct);
            if (installation is null || !installation.IsActive)
                return LiveMembership.Unavailable;

            Result<bool, Error> member = await _githubApi.IsOrgMemberAsync(
                installation.InstallationId, plan.Value.GitHubOrg!, githubLogin, ct);
            if (member.IsFailure) return LiveMembership.Unavailable;
            return member.Value ? LiveMembership.Member : LiveMembership.NotMember;
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex,
                "CompleteStep GITHUB live membership check failed for user={UserId} plan={PlanId} — rejecting complete",
                _user.UserId, planId);
            return LiveMembership.Unavailable;
        }
    }

    /// <summary>
    ///     Член ли текущий юзер GitHub-org'а плана по данным AuthService (тот же источник, что у
    ///     зелёной карточки шага). Fail-closed: план без org-привязки, AuthService недоступен или
    ///     любая ошибка → <c>false</c> (не завершаем шаг), как и TELEGRAM-проверка.
    /// </summary>
    private async Task<bool> IsConfirmedOrgMemberAsync(Guid planId, CancellationToken ct)
    {
        try
        {
            Result<AccessService.Domain.Plan, Error> plan = await _plans.GetByAsync(p => p.Id == planId, ct);
            if (plan.IsFailure || string.IsNullOrEmpty(plan.Value.GitHubOrg))
                return false;

            // Reverse-lookup (org → все user-id) — единственный готовый S2S-контракт; на текущем
            // масштабе (одна org) приемлемо. Точечный per-user endpoint — кандидат на оптимизацию.
            Result<UserIdsByGithubOrgResponse, Error> members =
                await _auth.GetUserIdsByGithubOrgAsync(plan.Value.GitHubOrg, ct);
            return members.IsSuccess && members.Value.UserIds.Contains(_user.UserId);
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex,
                "CompleteStep GITHUB org-membership check failed for user={UserId} plan={PlanId} — rejecting complete",
                _user.UserId, planId);
            return false;
        }
    }
}
