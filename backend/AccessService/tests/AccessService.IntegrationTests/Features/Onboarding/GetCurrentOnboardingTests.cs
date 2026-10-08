using System.Net.Http.Json;
using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;
using Wolverine.Tracking;

namespace AccessService.IntegrationTests.Features.Onboarding;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetCurrentOnboardingTests : AccessServiceTestsBase
{
    public GetCurrentOnboardingTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Returns_null_when_no_pending_onboarding()
    {
        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);

        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/onboarding/current/");
        resp.EnsureSuccessStatusCode();

        Envelope<CurrentOnboardingResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<CurrentOnboardingResponse>>();
        Assert.NotNull(env);
        Assert.False(env.IsError);
        Assert.Null(env.Result);
    }

    [Fact]
    public async Task Returns_pending_onboarding_after_plan_grant()
    {
        // Author creates plan + enables onboarding
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new PlanGrantCreated(
            GrantId: Guid.NewGuid(),
            UserId: studentId,
            PlanId: planId,
            PlanTier: nameof(PlanTier.FULL_ALL),
            PlanAuthorId: DefaultUserId,
            CourseId: null,
            IncludesFutureContent: true,
            Source: nameof(PlanGrantSource.ADMIN_GRANT),
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Capabilities: null));

        AuthenticateAs("platform-participant", studentId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/onboarding/current/");
        resp.EnsureSuccessStatusCode();

        Envelope<CurrentOnboardingResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<CurrentOnboardingResponse>>();
        Assert.NotNull(env);
        Assert.False(env.IsError);
        Assert.NotNull(env.Result);
        Assert.Equal(planId, env.Result.PlanId);
        Assert.Single(env.Result.Steps); // NOTIFICATIONS step
        Assert.Equal("NOTIFICATIONS", env.Result.Steps[0].StepType);
        Assert.NotNull(env.Result.State.CurrentStepId);
    }

    [Fact]
    public async Task Returns_null_when_flow_disabled_after_start()
    {
        // Setup: enabled flow → grant → onboarding row.
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new PlanGrantCreated(
            GrantId: Guid.NewGuid(),
            UserId: studentId,
            PlanId: planId,
            PlanTier: nameof(PlanTier.FULL_ALL),
            PlanAuthorId: DefaultUserId,
            CourseId: null,
            IncludesFutureContent: true,
            Source: nameof(PlanGrantSource.ADMIN_GRANT),
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Capabilities: null));

        // Author disables flow.
        await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: false));

        AuthenticateAs("platform-participant", studentId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/onboarding/current/");
        resp.EnsureSuccessStatusCode();

        Envelope<CurrentOnboardingResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<CurrentOnboardingResponse>>();
        Assert.NotNull(env);
        // Disabled flow → null (юзер не залипает в gate-петле).
        Assert.Null(env.Result);
    }

    [Fact]
    public async Task Heals_cursor_when_step_added_after_user_passed_the_end()
    {
        // #497 (прод-кейс Konstantin): юзер прошёл/проскипал все шаги (cursor=null,
        // «Завершить» не нажал), потом автор добавил шаг в flow. Раньше GET current
        // отдавал cursor=null → фронт рендерил CompletionView, а POST /complete/ бил
        // 409 has.pending.steps — тупик. Теперь курсор пере-наводится на pending шаг.
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid studentId = Guid.NewGuid();
        await GrantPlanAsync(planId, studentId);

        // Студент скипает единственный шаг (NOTIFICATIONS) → AdvanceTo → cursor=null.
        AuthenticateAs("platform-participant", studentId);
        CurrentOnboardingResponse before = (await GetCurrentAsync())!;
        Guid notificationsStepId = before.Steps[0].Id;
        await SkipStepAsync(planId, notificationsStepId);

        // Автор добавляет новый шаг в flow.
        AuthenticateAs("platform-author", DefaultUserId);
        Guid markdownStepId = await AddMarkdownStepAsync(planId, "Новый шаг");

        // Студент возвращается: курсор должен указывать на добавленный pending шаг.
        AuthenticateAs("platform-participant", studentId);
        CurrentOnboardingResponse healed = (await GetCurrentAsync())!;
        Assert.Equal(2, healed.Steps.Count);
        Assert.Equal(markdownStepId, healed.State.CurrentStepId);

        // Heal персистентен и flow завершаем: скип нового шага → complete проходит.
        await SkipStepAsync(planId, markdownStepId);
        HttpResponseMessage completeResp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/complete/", content: null);
        completeResp.EnsureSuccessStatusCode();
        Envelope<Guid>? completeEnv = await completeResp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(completeEnv);
        Assert.False(completeEnv.IsError);
    }

    [Fact]
    public async Task Heals_cursor_when_current_step_deleted_with_pending_remaining()
    {
        // #497, вторая ветка: курсор указывает на шаг, удалённый автором, при этом
        // остаются pending-шаги — курсор должен перепрыгнуть на ближайший pending.
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);
        Guid m1 = await AddMarkdownStepAsync(planId, "Шаг M1");
        Guid m2 = await AddMarkdownStepAsync(planId, "Шаг M2");

        Guid studentId = Guid.NewGuid();
        await GrantPlanAsync(planId, studentId);

        // Студент скипает NOTIFICATIONS → курсор на M1.
        AuthenticateAs("platform-participant", studentId);
        CurrentOnboardingResponse before = (await GetCurrentAsync())!;
        Guid notificationsStepId = before.Steps.Single(s => s.StepType == "NOTIFICATIONS").Id;
        await SkipStepAsync(planId, notificationsStepId);
        CurrentOnboardingResponse onM1 = (await GetCurrentAsync())!;
        Assert.Equal(m1, onM1.State.CurrentStepId);

        // Автор удаляет M1 — курсор студента повисает.
        AuthenticateAs("platform-author", DefaultUserId);
        HttpResponseMessage delResp = await AppHttpClient.DeleteAsync(
            $"/access/plans/{planId}/onboarding-flow/steps/{m1}/");
        delResp.EnsureSuccessStatusCode();

        AuthenticateAs("platform-participant", studentId);
        CurrentOnboardingResponse healed = (await GetCurrentAsync())!;
        Assert.Equal(m2, healed.State.CurrentStepId);
    }

    private async Task<CurrentOnboardingResponse?> GetCurrentAsync()
    {
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/onboarding/current/");
        resp.EnsureSuccessStatusCode();
        Envelope<CurrentOnboardingResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<CurrentOnboardingResponse>>();
        Assert.NotNull(env);
        Assert.False(env.IsError);
        return env.Result;
    }

    private async Task GrantPlanAsync(Guid planId, Guid studentId)
    {
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new PlanGrantCreated(
            GrantId: Guid.NewGuid(),
            UserId: studentId,
            PlanId: planId,
            PlanTier: nameof(PlanTier.FULL_ALL),
            PlanAuthorId: DefaultUserId,
            CourseId: null,
            IncludesFutureContent: true,
            Source: nameof(PlanGrantSource.ADMIN_GRANT),
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            Capabilities: null));
    }

    private async Task SkipStepAsync(Guid planId, Guid stepId)
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{stepId}/skip/", content: null);
        resp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> AddMarkdownStepAsync(Guid planId, string title)
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/steps/",
            new AddMarkdownStepRequest(title, "Содержимое шага", IsSkippable: true));
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return env!.Result;
    }

    private async Task<Guid> CreatePlanAsync()
    {
        AuthenticateAs("platform-author", DefaultUserId);
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
