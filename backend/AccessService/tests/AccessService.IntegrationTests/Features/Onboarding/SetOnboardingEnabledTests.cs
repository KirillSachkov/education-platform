using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Onboarding;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SetOnboardingEnabledTests : AccessServiceTestsBase
{
    public SetOnboardingEnabledTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Author_enables_onboarding_creates_flow_with_notifications_step()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanOnboardingFlow flow = await db.PlanOnboardingFlows
                .Include(f => f.Steps)
                .SingleAsync(f => f.PlanId == planId);

            Assert.True(flow.IsEnabled);
            Assert.Single(flow.Steps);
            Assert.Equal(PlanOnboardingStepType.NOTIFICATIONS, flow.Steps[0].Type);
        });
    }

    [Fact]
    public async Task Author_enables_onboarding_with_github_org_adds_github_step()
    {
        Guid planId = await CreatePlanAsync();

        // Установить GitHub org через PATCH /access/plans/{id}
        await AppHttpClient.PatchAsJsonAsync($"/access/plans/{planId}", new UpdatePlanRequest(
            DisplayName: null, ShortDescription: null, LongDescription: null,
            CoverFileId: null, Features: null, PriceCents: null, Currency: null,
            CourseIds: [], DisplayOrder: null, Capabilities: null,
            IsHighlighted: null, GithubOrgSlug: "test-org"));

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanOnboardingFlow flow = await db.PlanOnboardingFlows
                .Include(f => f.Steps)
                .SingleAsync(f => f.PlanId == planId);

            Assert.Equal(2, flow.Steps.Count);
            Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.NOTIFICATIONS);
            Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.GITHUB);
        });
    }

    [Fact]
    public async Task Re_enable_with_existing_steps_and_chat_binding_adds_telegram_without_concurrency_error()
    {
        // Repro: prod-сценарий — flow уже включался ранее, есть NOTIFICATIONS+GITHUB,
        // потом disable, потом юзер привязал TG чат, потом enable снова.
        // EF tracker неправильно помечал новый TELEGRAM step как Modified, не Added,
        // → DbUpdateConcurrencyException на UPDATE WHERE id=<новый guid> (0 rows).
        Guid planId = await CreatePlanAsync();
        await AppHttpClient.PatchAsJsonAsync($"/access/plans/{planId}", new UpdatePlanRequest(
            DisplayName: null, ShortDescription: null, LongDescription: null,
            CoverFileId: null, Features: null, PriceCents: null, Currency: null,
            CourseIds: [], DisplayOrder: null, Capabilities: null,
            IsHighlighted: null, GithubOrgSlug: "test-org"));

        // Enable #1 — без TG привязки. Создаются NOTIFICATIONS + GITHUB.
        HttpResponseMessage enable1 = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/", new SetOnboardingEnabledRequest(IsEnabled: true));
        Assert.Equal(HttpStatusCode.OK, enable1.StatusCode);

        // Disable.
        HttpResponseMessage disable = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/", new SetOnboardingEnabledRequest(IsEnabled: false));
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);

        // Симулируем что юзер привязал TG чат.
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);
        try
        {
            // Enable #2 — теперь должен добавиться TELEGRAM. Этот вызов раньше падал
            // с 500 DbUpdateConcurrencyException.
            HttpResponseMessage enable2 = await AppHttpClient.PutAsJsonAsync(
                $"/access/plans/{planId}/onboarding-flow/", new SetOnboardingEnabledRequest(IsEnabled: true));
            Assert.Equal(HttpStatusCode.OK, enable2.StatusCode);

            await ExecuteInDbAsync(async db =>
            {
                PlanOnboardingFlow flow = await db.PlanOnboardingFlows
                    .Include(f => f.Steps)
                    .SingleAsync(f => f.PlanId == planId);

                Assert.Equal(3, flow.Steps.Count);
                Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.NOTIFICATIONS);
                Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.GITHUB);
                Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.TELEGRAM);
            });
        }
        finally
        {
            Factory.TelegramClient.ActiveBindingsByPlanId.Clear();
        }
    }

    [Fact]
    public async Task Author_enables_onboarding_with_active_chat_binding_adds_telegram_step()
    {
        // Закрытый gap: до этого TG-шаг ensure'ился ТОЛЬКО на event chat_binding.bound_to_plan,
        // поэтому привязка чата ДО включения flow приводила к тому что шаг не появлялся.
        // Теперь SetEnabled на enable дёргает TelegramBotService HTTP-клиент.
        Guid planId = await CreatePlanAsync();
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);
        try
        {
            HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
                $"/access/plans/{planId}/onboarding-flow/",
                new SetOnboardingEnabledRequest(IsEnabled: true));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await ExecuteInDbAsync(async db =>
            {
                PlanOnboardingFlow flow = await db.PlanOnboardingFlows
                    .Include(f => f.Steps)
                    .SingleAsync(f => f.PlanId == planId);

                Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.NOTIFICATIONS);
                Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.TELEGRAM);
            });
        }
        finally
        {
            // Reset в finally — fixture shared между тестами, иначе protruded state
            // ломает другие тесты при assertion failure здесь.
            Factory.TelegramClient.ActiveBindingsByPlanId.Clear();
        }
    }

    [Fact]
    public async Task Author_enables_onboarding_without_chat_binding_does_not_add_telegram_step()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanOnboardingFlow flow = await db.PlanOnboardingFlows
                .Include(f => f.Steps)
                .SingleAsync(f => f.PlanId == planId);

            Assert.DoesNotContain(flow.Steps, s => s.Type == PlanOnboardingStepType.TELEGRAM);
        });
    }

    [Fact]
    public async Task Author_enables_onboarding_when_telegram_service_unavailable_skips_step_softly()
    {
        // Soft-degrade: TG service down → enable не падает, шаг просто не добавляется.
        // Следующий event chat_binding.bound_to_plan ensure'ит шаг как обычно.
        Guid planId = await CreatePlanAsync();
        Factory.TelegramClient.ShouldFail = true;
        try
        {
            HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
                $"/access/plans/{planId}/onboarding-flow/",
                new SetOnboardingEnabledRequest(IsEnabled: true));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            await ExecuteInDbAsync(async db =>
            {
                PlanOnboardingFlow flow = await db.PlanOnboardingFlows
                    .Include(f => f.Steps)
                    .SingleAsync(f => f.PlanId == planId);

                Assert.True(flow.IsEnabled);
                Assert.Contains(flow.Steps, s => s.Type == PlanOnboardingStepType.NOTIFICATIONS);
                Assert.DoesNotContain(flow.Steps, s => s.Type == PlanOnboardingStepType.TELEGRAM);
            });
        }
        finally
        {
            Factory.TelegramClient.ShouldFail = false;
        }
    }

    [Fact]
    public async Task Disable_onboarding_keeps_steps_but_flag_off()
    {
        Guid planId = await CreatePlanAsync();
        await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanOnboardingFlow flow = await db.PlanOnboardingFlows
                .Include(f => f.Steps)
                .SingleAsync(f => f.PlanId == planId);

            Assert.False(flow.IsEnabled);
            // Шаги остаются — конфиг автора сохраняется, чтобы не пересоздавать при повторном включении.
            Assert.NotEmpty(flow.Steps);
        });
    }

    [Fact]
    public async Task Foreign_author_cannot_enable_onboarding()
    {
        Guid planId = await CreatePlanAsync();

        AuthenticateAs("platform-author", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));

        // .RequirePermissions wraps Result.Error → Envelope. Не путать с 401/403:
        // ownership-check возвращает access.denied → 403-ish (через EnvelopeError).
        // bool — value type, не маршалится из null. Читаем как object для error envelope.
        Envelope<object>? envelope = await response.Content.ReadFromJsonAsync<Envelope<object>>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest req = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "test-plan",
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
}
