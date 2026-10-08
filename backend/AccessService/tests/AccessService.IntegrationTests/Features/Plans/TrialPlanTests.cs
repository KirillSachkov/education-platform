using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Issue #595 — пробный доступ через <see cref="CreatePlanRequest.IsTrial"/>:
/// срок из конфига (default 30), tier форсится в FULL_ALL, singleton на платформу.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TrialPlanTests : AccessServiceTestsBase
{
    public TrialPlanTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreatePlanAsync(CreatePlanRequest request)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        response.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope.Result;
    }

    private static CreatePlanRequest TrialRequest(string slug = "trial-month") => new(
        Tier: nameof(PlanTier.FULL_ALL),
        Slug: slug,
        DisplayName: "Пробный месяц",
        ShortDescription: "Попробуй всё на 30 дней",
        LongDescription: null,
        CoverFileId: null,
        Features: null,
        PriceCents: 100_000,
        Currency: "RUB",
        CourseIds: [],
        DisplayOrder: 0,
        IsTrial: true);

    private static CreatePlanRequest LifetimeRequest(string slug = "full-access") => new(
        Tier: nameof(PlanTier.FULL_ALL),
        Slug: slug,
        DisplayName: "Полный доступ",
        ShortDescription: "Доступ ко всему навсегда",
        LongDescription: null,
        CoverFileId: null,
        Features: null,
        PriceCents: 990_000,
        Currency: "RUB",
        CourseIds: [],
        DisplayOrder: 0);

    [Fact]
    public async Task Create_with_IsTrial_sets_duration_from_config_and_full_all_tier()
    {
        // Tier намеренно подаём как FULL_ALL — но даже если бы прислали другой, IsTrial форсит FULL_ALL.
        Guid planId = await CreatePlanAsync(TrialRequest());

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanTier.FULL_ALL, plan.Tier);
            Assert.Equal(PlanOfferType.FULL_ACCESS, plan.OfferType);
            Assert.True(plan.IsTrial);
            // Срок берётся из конфига (Access:TrialDurationDays), default 30 — автор не вводит.
            Assert.Equal(30, plan.TrialDurationDays);
        });
    }

    [Fact]
    public async Task GetMyPlans_returns_trial_duration_days_for_trial_plan()
    {
        Guid planId = await CreatePlanAsync(TrialRequest());

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/");
        response.EnsureSuccessStatusCode();

        Envelope<List<PlanReadModel>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<PlanReadModel>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        PlanReadModel? trial = envelope.Result!.SingleOrDefault(p => p.Id == planId);
        Assert.NotNull(trial);
        Assert.Equal(30, trial!.TrialDurationDays);
    }

    [Fact]
    public async Task Second_trial_plan_on_platform_cannot_be_published()
    {
        Guid firstId = await CreatePlanAsync(TrialRequest("trial-one"));
        HttpResponseMessage publishFirst = await AppHttpClient.PostAsync(
            $"/access/plans/{firstId}/publish", content: null);
        publishFirst.EnsureSuccessStatusCode();

        Guid secondId = await CreatePlanAsync(TrialRequest("trial-two"));
        HttpResponseMessage publishSecond = await AppHttpClient.PostAsync(
            $"/access/plans/{secondId}/publish", content: null);

        Assert.Equal(HttpStatusCode.Conflict, publishSecond.StatusCode);

        Envelope? envelope = await publishSecond.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "plan.trial.duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Trial_and_lifetime_full_all_coexist_both_publish()
    {
        Guid trialId = await CreatePlanAsync(TrialRequest());
        Guid lifetimeId = await CreatePlanAsync(LifetimeRequest());

        HttpResponseMessage publishTrial = await AppHttpClient.PostAsync(
            $"/access/plans/{trialId}/publish", content: null);
        Assert.Equal(HttpStatusCode.OK, publishTrial.StatusCode);

        HttpResponseMessage publishLifetime = await AppHttpClient.PostAsync(
            $"/access/plans/{lifetimeId}/publish", content: null);
        Assert.Equal(HttpStatusCode.OK, publishLifetime.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan trial = await db.Plans.SingleAsync(p => p.Id == trialId);
            Plan lifetime = await db.Plans.SingleAsync(p => p.Id == lifetimeId);
            Assert.True(trial.IsPublic);
            Assert.True(lifetime.IsPublic);
            Assert.True(trial.IsTrial);
            Assert.False(lifetime.IsTrial);
        });
    }

    [Fact]
    public async Task Trial_plan_cannot_be_updated_as_regular_plan()
    {
        Guid trialId = await CreatePlanAsync(TrialRequest());

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{trialId}",
            new UpdatePlanRequest(
                DisplayName: "Новое название",
                ShortDescription: null,
                LongDescription: null,
                CoverFileId: null,
                Features: null,
                PriceCents: null,
                Currency: null,
                CourseIds: null,
                DisplayOrder: null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.trial.read_only", StringComparison.Ordinal));
    }

    // Minimal read-model for GET /access/plans/ — only the fields the trial test asserts on.
    private sealed record PlanReadModel(Guid Id, int? TrialDurationDays);
}
