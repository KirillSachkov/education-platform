using System.Diagnostics.Metrics;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using AccessService.Core.Database;
using AccessService.Core.Diagnostics;
using AccessService.Core.Features.Integrations.GitHubApp;
using AccessService.Core.Features.Integrations.GitHubApp.UseCases;
using AccessService.Core.Features.Onboarding.Handlers;
using AccessService.Core.Features.TgJoinReminders.Handlers;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AccessService.Domain.Onboarding;
using AccessService.Domain.TgJoinReminders;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Access.Events;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using SharedKernel.Exceptions;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.IntegrationTests.Features.Reliability;

public sealed class PersistenceFailureReliabilityTests
{
    private const string WebhookSecret = "access-webhook-test-secret";
    private static readonly DateTimeOffset Now = new(2026, 7, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Chat_binding_bound_save_failure_escapes_for_wolverine_retry()
    {
        Guid planId = Guid.CreateVersion7();
        PlanOnboardingFlow flow = EnabledFlow(planId);
        IPlanOnboardingFlowsRepository flows = FlowRepository(flow);

        await Assert.ThrowsAsync<TransientException>(() => ChatBindingBoundToPlanHandler.HandleAsync(
            new ChatBindingBoundToPlan(Guid.CreateVersion7(), planId, -100123),
            flows,
            FailingTransactions(),
            TimeProvider.System,
            Substitute.For<ILogger<PlanOnboardingFlow>>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task Chat_binding_unbound_save_failure_escapes_for_wolverine_retry()
    {
        Guid planId = Guid.CreateVersion7();
        PlanOnboardingFlow flow = EnabledFlow(planId, PlanOnboardingStepType.TELEGRAM);
        IPlanOnboardingFlowsRepository flows = FlowRepository(flow);

        await Assert.ThrowsAsync<TransientException>(() => ChatBindingUnboundFromPlanHandler.HandleAsync(
            new ChatBindingUnboundFromPlan(Guid.CreateVersion7(), planId, -100123, 0),
            flows,
            FailingTransactions(),
            TimeProvider.System,
            Substitute.For<ILogger<PlanOnboardingFlow>>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task Chat_membership_confirmed_save_failure_escapes_for_wolverine_retry()
    {
        Guid planId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        PlanOnboardingFlow flow = EnabledFlow(planId, PlanOnboardingStepType.TELEGRAM);
        UserPlanOnboarding onboarding = UserPlanOnboarding.Start(userId, planId, Now);
        onboarding.SetCurrentStep(flow.Steps.Single().Id);
        IUserPlanOnboardingsRepository onboardings = Substitute.For<IUserPlanOnboardingsRepository>();
        onboardings.GetAsync(userId, planId, Arg.Any<CancellationToken>()).Returns(onboarding);

        await Assert.ThrowsAsync<TransientException>(() => ChatMembershipConfirmedHandler.HandleAsync(
            new ChatMemberConfirmed(userId, planId, -100123, Now),
            onboardings,
            FlowRepository(flow),
            FailingTransactions(),
            Substitute.For<ILogger<UserPlanOnboarding>>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task Plan_grant_created_onboarding_save_failure_escapes_for_wolverine_retry()
    {
        Plan plan = Plan.Create(
            Guid.CreateVersion7(),
            PlanTier.FULL_ALL,
            PlanSlug.Of($"reliability-{Guid.CreateVersion7():N}").Value,
            PlanDisplayName.Of("Reliability plan").Value,
            [],
            null).Value;
        PlanOnboardingFlow flow = EnabledFlow(plan.Id, PlanOnboardingStepType.NOTIFICATIONS);
        IPlansRepository plans = Substitute.For<IPlansRepository>();
        plans.GetByAsync(
                Arg.Any<Expression<Func<Plan, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<Plan, Error>(plan));
        IUserPlanOnboardingsRepository onboardings = Substitute.For<IUserPlanOnboardingsRepository>();
        onboardings.ExistsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        await Assert.ThrowsAsync<TransientException>(() => PlanGrantCreatedOnboardingHandler.HandleAsync(
            BuildPlanGrantCreated(plan.Id),
            plans,
            FlowRepository(flow),
            onboardings,
            FailingTransactions(),
            TimeProvider.System,
            Substitute.For<ILogger<UserPlanOnboarding>>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task Vcs_installation_created_save_failure_escapes_for_wolverine_retry()
    {
        Guid planId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        PlanOnboardingFlow flow = EnabledFlow(planId, PlanOnboardingStepType.GITHUB_REVIEW_APP);
        UserPlanOnboarding onboarding = UserPlanOnboarding.Start(userId, planId, Now);
        onboarding.SetCurrentStep(flow.Steps.Single().Id);
        IUserPlanOnboardingsRepository onboardings = Substitute.For<IUserPlanOnboardingsRepository>();
        onboardings.GetManyByAsync(
                Arg.Any<Expression<Func<UserPlanOnboarding, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns([onboarding]);

        await Assert.ThrowsAsync<TransientException>(() => VcsInstallationCreatedHandler.HandleAsync(
            new VcsInstallationCreated(userId, 42, "student", "User", Now),
            onboardings,
            FlowRepository(flow),
            FailingTransactions(),
            Substitute.For<ILogger<UserPlanOnboarding>>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task Chat_member_confirmed_reminder_save_failure_escapes_for_wolverine_retry()
    {
        Guid planId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        TgJoinReminder reminder = TgJoinReminder.Create(userId, planId, Guid.CreateVersion7(), Now);
        ITgJoinRemindersRepository reminders = Substitute.For<ITgJoinRemindersRepository>();
        reminders.GetByUserAndPlanAsync(userId, planId, Arg.Any<CancellationToken>()).Returns(reminder);

        await Assert.ThrowsAsync<TransientException>(() => ChatMemberConfirmedTgJoinHandler.HandleAsync(
            new ChatMemberConfirmed(userId, planId, -100123, Now),
            reminders,
            FailingTransactions(),
            TimeProvider.System,
            Substitute.For<ILogger<TgJoinReminder>>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task Plan_grant_created_reminder_save_failure_escapes_for_wolverine_retry()
    {
        ITgJoinRemindersRepository reminders = Substitute.For<ITgJoinRemindersRepository>();
        reminders.ExistsAsync(
                Arg.Any<Expression<Func<TgJoinReminder, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(false);
        ITelegramBotServiceClient telegram = Substitute.For<ITelegramBotServiceClient>();
        telegram.HasActiveChatBindingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<bool, Error>(true));

        await Assert.ThrowsAsync<TransientException>(() => PlanGrantCreatedTgJoinHandler.HandleAsync(
            BuildPlanGrantCreated(Guid.CreateVersion7(), capabilities: ["COMMUNITY_ACCESS"]),
            reminders,
            telegram,
            Substitute.For<IOutboxService>(),
            FailingTransactions(),
            TimeProvider.System,
            Substitute.For<ILogger<TgJoinReminder>>(),
            CancellationToken.None));
    }

    [Fact]
    public async Task Installation_webhook_save_failure_returns_service_unavailable()
    {
        IAuthorGithubInstallationsRepository installations = Substitute.For<IAuthorGithubInstallationsRepository>();
        AuthorGithubInstallation installation = AuthorGithubInstallation.Create(
            Guid.CreateVersion7(), 42, "example-org", Now);
        installations.GetByInstallationIdAsync(42, Arg.Any<CancellationToken>()).Returns(installation);
        using TestMeterFactory meterFactory = new();
        GitHubWebhookHandler handler = CreateWebhookHandler(
            installations,
            Substitute.For<IGithubOrgInvitationsRepository>(),
            meterFactory);
        byte[] body = Encoding.UTF8.GetBytes("{\"action\":\"suspend\",\"installation\":{\"id\":42}}");

        Microsoft.AspNetCore.Http.IResult result = await handler.HandleAsync(
            body, ComputeSignature(body), "installation", CancellationToken.None);

        IStatusCodeHttpResult status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    [Fact]
    public async Task Organization_webhook_save_failure_returns_service_unavailable()
    {
        IGithubOrgInvitationsRepository invitations = Substitute.For<IGithubOrgInvitationsRepository>();
        GithubOrgInvitation invitation = GithubOrgInvitation.CreatePending(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "student", "example-org", 123, Now);
        invitations.GetPendingByOrgLoginAsync(
                "example-org", "student", Arg.Any<CancellationToken>())
            .Returns(invitation);
        using TestMeterFactory meterFactory = new();
        GitHubWebhookHandler handler = CreateWebhookHandler(
            Substitute.For<IAuthorGithubInstallationsRepository>(),
            invitations,
            meterFactory);
        byte[] body = Encoding.UTF8.GetBytes(
            "{\"action\":\"member_added\",\"organization\":{\"login\":\"example-org\"}," +
            "\"membership\":{\"user\":{\"login\":\"student\"}}}");

        Microsoft.AspNetCore.Http.IResult result = await handler.HandleAsync(
            body, ComputeSignature(body), "organization", CancellationToken.None);

        IStatusCodeHttpResult status = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    private static IPlanOnboardingFlowsRepository FlowRepository(PlanOnboardingFlow flow)
    {
        IPlanOnboardingFlowsRepository flows = Substitute.For<IPlanOnboardingFlowsRepository>();
        flows.GetByAsync(
                Arg.Any<Expression<Func<PlanOnboardingFlow, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanOnboardingFlow, Error>(flow));
        return flows;
    }

    private static PlanOnboardingFlow EnabledFlow(
        Guid planId,
        PlanOnboardingStepType? stepType = null)
    {
        PlanOnboardingFlow flow = PlanOnboardingFlow.Create(planId, Now);
        flow.Enable(Now);
        if (stepType is { } type)
        {
            flow.EnsureAutoStep(type, Now);
        }

        return flow;
    }

    private static ITransactionManager FailingTransactions()
    {
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));
        return transactions;
    }

    private static PlanGrantCreated BuildPlanGrantCreated(
        Guid planId,
        IReadOnlyList<string>? capabilities = null) =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            planId,
            nameof(PlanTier.FULL_ALL),
            Guid.CreateVersion7(),
            null,
            true,
            nameof(PlanGrantSource.PURCHASE),
            null,
            Now,
            null,
            capabilities,
            PlanName: "Full access");

    private static GitHubWebhookHandler CreateWebhookHandler(
        IAuthorGithubInstallationsRepository installations,
        IGithubOrgInvitationsRepository invitations,
        IMeterFactory meterFactory) =>
        new(
            installations,
            invitations,
            FailingTransactions(),
            TimeProvider.System,
            Options.Create(new GitHubAppOptions { WebhookSecret = WebhookSecret }),
            new OnboardingMetrics(meterFactory),
            Substitute.For<ILogger<GitHubWebhookHandler>>());

    private static string ComputeSignature(byte[] body)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(WebhookSecret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = [];

        public Meter Create(MeterOptions options)
        {
            Meter meter = new(options);
            _meters.Add(meter);
            return meter;
        }

        public void Dispose()
        {
            foreach (Meter meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}
