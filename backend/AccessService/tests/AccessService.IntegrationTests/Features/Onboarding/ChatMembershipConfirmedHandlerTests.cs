using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using System.Net.Http.Json;
using Wolverine;
using Wolverine.Tracking;

namespace AccessService.IntegrationTests.Features.Onboarding;

/// <summary>
///     L1 handler tests for <see cref="ChatMembershipConfirmedHandler"/> (epic #397).
///     Invokes the handler in-process via <c>InvokeMessageAndWaitAsync</c>. Covers:
///     pending TELEGRAM step completes + cursor advances; idempotent on repeat; no-op when
///     no onboarding / no TELEGRAM step / already completed.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class ChatMembershipConfirmedHandlerTests : AccessServiceTestsBase
{
    public ChatMembershipConfirmedHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Completes_pending_telegram_step_and_advances_cursor()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        Guid telegramStepId = await TelegramStepIdAsync(planId);

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new ChatMemberConfirmed(
            PlatformUserId: studentId,
            PlanId: planId,
            TelegramChatId: -100123456,
            OccurredAt: DateTimeOffset.UtcNow));

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.Contains(telegramStepId, ob.CompletedStepIds);
            // Cursor advanced past the TELEGRAM step (to next pending or null).
            Assert.NotEqual(telegramStepId, ob.CurrentStepId);
            Assert.Null(ob.CompletedAt); // not auto-finalized
        });
    }

    [Fact]
    public async Task Is_idempotent_on_repeat()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid telegramStepId = await TelegramStepIdAsync(planId);

        IHost host = Services.GetRequiredService<IHost>();
        ChatMemberConfirmed evt = new(studentId, planId, -100123456, DateTimeOffset.UtcNow);

        await host.InvokeMessageAndWaitAsync(evt);
        await host.InvokeMessageAndWaitAsync(evt); // second time — no-op

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.Single(ob.CompletedStepIds, telegramStepId);
        });
    }

    [Fact]
    public async Task No_op_when_no_onboarding_row()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid(); // no onboarding seeded

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new ChatMemberConfirmed(
            studentId, planId, -100123456, DateTimeOffset.UtcNow));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.UserPlanOnboardings.CountAsync(o => o.UserId == studentId);
            Assert.Equal(0, count); // handler never creates rows
        });
    }

    [Fact]
    public async Task No_op_when_flow_has_no_telegram_step()
    {
        // Plan onboarding enabled WITHOUT a chat-binding → no TELEGRAM step ensured,
        // only NOTIFICATIONS. The handler must silently no-op.
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new ChatMemberConfirmed(
            studentId, planId, -100123456, DateTimeOffset.UtcNow));

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.Empty(ob.CompletedStepIds);
        });
    }

    [Fact]
    public async Task No_op_when_onboarding_already_completed()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = UserPlanOnboarding.Start(studentId, planId, DateTimeOffset.UtcNow);
            // Mark every step done + complete the onboarding.
            List<Guid> stepIds = await db.PlanOnboardingSteps
                .Where(s => s.PlanId == planId)
                .Select(s => s.Id)
                .ToListAsync();
            foreach (Guid id in stepIds) ob.CompleteStep(id);
            ob.Complete(stepIds, DateTimeOffset.UtcNow);
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
        });

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new ChatMemberConfirmed(
            studentId, planId, -100123456, DateTimeOffset.UtcNow));

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.NotNull(ob.CompletedAt); // stayed completed, untouched
        });
    }

    private async Task<Guid> TelegramStepIdAsync(Guid planId) =>
        await ExecuteInDbAsync(async db => await db.PlanOnboardingSteps
            .Where(s => s.PlanId == planId && s.Type == PlanOnboardingStepType.TELEGRAM)
            .Select(s => s.Id)
            .SingleAsync());

    private async Task SeedOnboardingAtFirstStepAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            List<PlanOnboardingStep> steps = await db.PlanOnboardingSteps
                .Where(s => s.PlanId == planId)
                .ToListAsync();
            Guid firstStepId = steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id)
                .First();

            UserPlanOnboarding ob = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            ob.SetCurrentStep(firstStepId);
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> CreatePlanWithTelegramStepAsync()
    {
        Guid planId = await CreatePlanAsync();
        // Make SetEnabled ensure a TELEGRAM step by reporting an active chat-binding.
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);
        await EnableOnboardingAsync(planId);
        return planId;
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest req = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"plan-{Guid.NewGuid():N}",
            DisplayName: "Test Plan",
            ShortDescription: "desc",
            LongDescription: "long",
            CoverFileId: null,
            Features: new[] { "f1" },
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/access/plans/", req);
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return env!.Result;
    }

    private async Task EnableOnboardingAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));
        resp.EnsureSuccessStatusCode();
    }
}
