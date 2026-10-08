using AccessService.Contracts.Onboarding;
using AccessService.Core.Database;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AccessService.Domain.Onboarding;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Admin;

/// <summary>
///     Support path for a user whose GitHub OAuth projection is stale while their real
///     organization membership is already active. The step is completed only after the
///     plan author's GitHub App confirms the supplied login as an active org member.
/// </summary>
public sealed class RecheckGithubMembershipForUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/access/admin/users/{userId:guid}/plans/{planId:guid}/github/recheck/",
                async Task<EndpointResult<RecheckGithubMembershipResponse>> (
                    [FromRoute] Guid userId,
                    [FromRoute] Guid planId,
                    [FromBody] RecheckGithubMembershipRequest request,
                    [FromServices] RecheckGithubMembershipForUserHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(
                        new RecheckGithubMembershipForUserCommand(userId, planId, request.GithubLogin), ct))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed record RecheckGithubMembershipForUserCommand(
    Guid UserId,
    Guid PlanId,
    string? GithubLogin) : ICommand;

public sealed class RecheckGithubMembershipForUserCommandValidator
    : AbstractValidator<RecheckGithubMembershipForUserCommand>
{
    public RecheckGithubMembershipForUserCommandValidator()
    {
        RuleFor(command => command.GithubLogin)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithError(GitHubAppErrors.UserGithubLoginInvalid())
            .Must(IsValidGithubLogin)
            .WithError(GitHubAppErrors.UserGithubLoginInvalid());
    }

    private static bool IsValidGithubLogin(string? value)
    {
        string login = value?.Trim() ?? string.Empty;
        if (login.Length is 0 or > 39 || login[0] == '-' || login[^1] == '-')
            return false;

        bool previousDash = false;
        foreach (char character in login)
        {
            char normalized = char.ToLowerInvariant(character);
            bool alphanumeric = normalized is >= 'a' and <= 'z' or >= '0' and <= '9';
            bool dash = character == '-';
            if ((!alphanumeric && !dash) || (dash && previousDash))
                return false;

            previousDash = dash;
        }

        return true;
    }
}

public sealed class RecheckGithubMembershipForUserHandler
    : ICommandHandler<RecheckGithubMembershipResponse, RecheckGithubMembershipForUserCommand>
{
    private const string STATUS_MEMBER = "member";
    private const string STATUS_NOT_MEMBER = "not_member";
    private const string STATUS_UNKNOWN = "unknown";

    private readonly IUserPlanOnboardingsRepository _onboardings;
    private readonly IPlanOnboardingFlowsRepository _flows;
    private readonly IPlansRepository _plans;
    private readonly IAuthorGithubInstallationsRepository _installations;
    private readonly IGitHubAppApiClient _github;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<RecheckGithubMembershipForUserCommand> _validator;
    private readonly ILogger<RecheckGithubMembershipForUserHandler> _logger;

    public RecheckGithubMembershipForUserHandler(
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        IPlansRepository plans,
        IAuthorGithubInstallationsRepository installations,
        IGitHubAppApiClient github,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<RecheckGithubMembershipForUserCommand> validator,
        ILogger<RecheckGithubMembershipForUserHandler> logger)
    {
        _onboardings = onboardings;
        _flows = flows;
        _plans = plans;
        _installations = installations;
        _github = github;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<RecheckGithubMembershipResponse, Error>> Handle(
        RecheckGithubMembershipForUserCommand cmd,
        CancellationToken ct)
    {
        ValidationResult validation = await _validator.ValidateAsync(cmd, ct);
        if (!validation.IsValid)
            return validation.ToError();

        string login = cmd.GithubLogin!.Trim();

        UserPlanOnboarding? onboarding = await _onboardings.GetAsync(cmd.UserId, cmd.PlanId, ct);
        if (onboarding is null) return OnboardingErrors.OnboardingNotFound();

        Result<PlanOnboardingFlow, Error> flowResult = await _flows.GetByAsync(
            flow => flow.PlanId == cmd.PlanId, ct);
        if (flowResult.IsFailure) return flowResult.Error;

        PlanOnboardingFlow flow = flowResult.Value;
        PlanOnboardingStep? githubStep = flow.Steps
            .FirstOrDefault(step => step.Type == PlanOnboardingStepType.GITHUB);
        if (githubStep is null)
            return Outcome(cmd, STATUS_UNKNOWN, completed: false);

        if (onboarding.CompletedStepIds.Contains(githubStep.Id))
        {
            onboarding.AdvanceTo(OrderedStepIds(flow));
            UnitResult<Error> advanceSave = await _transactions.SaveChangesAsync(ct);
            if (advanceSave.IsFailure) return advanceSave.Error;
            return Outcome(cmd, STATUS_MEMBER, completed: true);
        }

        Result<AccessService.Domain.Plan, Error> planResult = await _plans.GetByAsync(
            plan => plan.Id == cmd.PlanId, ct);
        if (planResult.IsFailure) return planResult.Error;

        AccessService.Domain.Plan plan = planResult.Value;
        if (string.IsNullOrWhiteSpace(plan.GitHubOrg))
            return Outcome(cmd, STATUS_UNKNOWN, completed: false);

        AuthorGithubInstallation? installation = await _installations.GetByAuthorAsync(plan.AuthorId, ct);
        if (installation is null || !installation.IsActive)
            return Outcome(cmd, STATUS_UNKNOWN, completed: false);

        Result<bool, Error> membership = await _github.IsOrgMemberAsync(
            installation.InstallationId, plan.GitHubOrg, login, ct);
        if (membership.IsFailure)
        {
            _logger.LogWarning(
                "GitHub membership recheck failed for target={TargetUserId} plan={PlanId}: {Error}",
                cmd.UserId, cmd.PlanId, membership.Error.GetMessage());
            return Outcome(cmd, STATUS_UNKNOWN, completed: false);
        }

        if (!membership.Value)
            return Outcome(cmd, STATUS_NOT_MEMBER, completed: false);

        UnitResult<Error> completion = onboarding.CompleteStep(githubStep.Id);
        if (completion.IsFailure) return completion.Error;

        onboarding.AdvanceTo(OrderedStepIds(flow));
        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure) return save.Error;

        return Outcome(cmd, STATUS_MEMBER, completed: true);
    }

    private RecheckGithubMembershipResponse Outcome(
        RecheckGithubMembershipForUserCommand cmd,
        string status,
        bool completed)
    {
        _logger.LogInformation(
            "support-action {Action} admin={AdminUserId} target={TargetUserId} plan={PlanId} github={GithubLogin} outcome={Outcome} completed={Completed}",
            "github-recheck", _user.UserId, cmd.UserId, cmd.PlanId, cmd.GithubLogin, status, completed);
        return new RecheckGithubMembershipResponse(completed, status);
    }

    private static IEnumerable<Guid> OrderedStepIds(PlanOnboardingFlow flow) =>
        flow.Steps
            .OrderBy(step => step.SortOrder.Value, StringComparer.Ordinal)
            .Select(step => step.Id);
}
