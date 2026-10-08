using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Access.Events;
using System.Net.Http.Json;
using Wolverine.Tracking;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Onboarding;

/// <summary>
///     L1 — handler logic. PlanGrantCreated → должен создаться UserPlanOnboarding
///     если flow.is_enabled=true и есть шаги. Иначе — no-op.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanGrantCreatedOnboardingHandlerTests : AccessServiceTestsBase
{
    public PlanGrantCreatedOnboardingHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Creates_user_plan_onboarding_when_flow_enabled()
    {
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);
        Guid studentId = Guid.NewGuid();

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(BuildPlanGrantCreated(planId, studentId));

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding onboarding = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.False(onboarding.IsCompleted);
            Assert.NotNull(onboarding.CurrentStepId);
        });
    }

    [Fact]
    public async Task Noop_when_flow_disabled()
    {
        Guid planId = await CreatePlanAsync();
        // Flow создаём НЕ включая → IsEnabled=false
        Guid studentId = Guid.NewGuid();

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(BuildPlanGrantCreated(planId, studentId));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.UserPlanOnboardings.CountAsync(o => o.UserId == studentId);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public async Task Noop_when_flow_not_configured()
    {
        Guid planId = await CreatePlanAsync();
        Guid studentId = Guid.NewGuid();

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(BuildPlanGrantCreated(planId, studentId));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.UserPlanOnboardings.CountAsync();
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public async Task Idempotent_on_repeat_grant_for_same_user_plan()
    {
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);
        Guid studentId = Guid.NewGuid();

        IHost host = Services.GetRequiredService<IHost>();
        PlanGrantCreated msg = BuildPlanGrantCreated(planId, studentId);

        await host.InvokeMessageAndWaitAsync(msg);
        await host.InvokeMessageAndWaitAsync(msg);

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.UserPlanOnboardings.CountAsync(
                o => o.UserId == studentId && o.PlanId == planId);
            Assert.Equal(1, count);
        });
    }

    [Fact]
    public async Task Trial_grant_uses_canonical_lifetime_full_access_onboarding()
    {
        Guid lifetimePlanId = await CreatePlanAsync("lifetime-full", isTrial: false);
        await PublishPlanAsync(lifetimePlanId);
        await EnableOnboardingAsync(lifetimePlanId);

        Guid trialPlanId = await CreatePlanAsync("trial-month", isTrial: true);
        await PublishPlanAsync(trialPlanId);
        await EnableOnboardingAsync(trialPlanId);

        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(
            BuildPlanGrantCreated(trialPlanId, studentId, nameof(PlanGrantSource.PURCHASE)));

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding onboarding = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId);

            Assert.Equal(lifetimePlanId, onboarding.PlanId);
            Assert.False(await db.UserPlanOnboardings
                .AnyAsync(o => o.UserId == studentId && o.PlanId == trialPlanId));
        });
    }

    private Task<Guid> CreatePlanAsync() => CreatePlanAsync($"plan-{Guid.NewGuid():N}", isTrial: false);

    private async Task<Guid> CreatePlanAsync(string slug, bool isTrial)
    {
        CreatePlanRequest req = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"{slug}-{Guid.NewGuid():N}",
            DisplayName: isTrial ? "Пробный месяц" : "Test Plan",
            ShortDescription: "desc",
            LongDescription: "long",
            CoverFileId: null,
            Features: new[] { "f1" },
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            IsTrial: isTrial);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/access/plans/", req);
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return env!.Result;
    }

    private async Task PublishPlanAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        resp.EnsureSuccessStatusCode();
    }

    private async Task EnableOnboardingAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));
        resp.EnsureSuccessStatusCode();
    }

    private static PlanGrantCreated BuildPlanGrantCreated(
        Guid planId,
        Guid userId,
        string? source = null) =>
        new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: planId,
            PlanTier: nameof(PlanTier.FULL_ALL),
            PlanAuthorId: AccessServiceTestsBase.DefaultUserId,
            CourseId: null,
            IncludesFutureContent: true,
            Source: source ?? nameof(PlanGrantSource.ADMIN_GRANT),
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Capabilities: null);
}
