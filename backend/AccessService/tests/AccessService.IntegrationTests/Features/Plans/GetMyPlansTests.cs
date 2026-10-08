using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Dtos;
using AccessService.Contracts.Plans.Requests;
using AccessService.Contracts.TrainerPro;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMyPlansTests : AccessServiceTestsBase
{
    public GetMyPlansTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreatePlanAsync(string slug, string displayName = "Полный доступ")
    {
        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: displayName,
            ShortDescription: "Доступ ко всему",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 990_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope.Result;
    }

    [Fact]
    public async Task Returns_only_caller_authors_plans()
    {
        // First user (default) creates a plan.
        await CreatePlanAsync("user-a-plan");

        // Switch to a different author.
        Guid otherUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", otherUserId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PlanDto>>? envelope = await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
    }

    [Fact]
    public async Task Owner_returns_all_platform_plans()
    {
        Guid userAPlanId = await CreatePlanAsync("user-a-plan");

        Guid userBId = Guid.NewGuid();
        AuthenticateAs("platform-author", userBId);
        Guid userBPlanId = await CreatePlanAsync("user-b-plan");

        AuthenticateAs("platform-owner", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Equal(2, envelope.Result.Count);
        Assert.Contains(envelope.Result, p => p.Id == userAPlanId);
        Assert.Contains(envelope.Result, p => p.Id == userBPlanId);
    }

    [Fact]
    public async Task Returns_all_plans_of_caller_including_archived()
    {
        Guid plan1Id = await CreatePlanAsync("plan-active");
        Guid plan2Id = await CreatePlanAsync("plan-archived");

        // Archive the second plan.
        HttpResponseMessage archive = await AppHttpClient.PostAsync($"/access/plans/{plan2Id}/archive", content: null);
        archive.EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PlanDto>>? envelope = await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Equal(2, envelope.Result.Count);

        Assert.Contains(envelope.Result, p => p.Id == plan1Id && p.ArchivedAt is null && p.IsActive);
        Assert.Contains(envelope.Result, p => p.Id == plan2Id && p.ArchivedAt is not null && !p.IsActive);
    }

    [Fact]
    public async Task Excludes_trainer_scoped_plans()
    {
        // #674: the trainer subscription is managed only via /access/trainer-pro/* — it must NOT appear
        // in the general author plan-management list (alongside being hidden from the public catalog).
        Guid platformPlanId = await CreatePlanAsync("platform-plan");
        Guid trainerOfferId = await CreateTrainerOfferAsync("trainer-pro-monthly");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PlanDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Contains(envelope.Result, p => p.Id == platformPlanId);
        Assert.DoesNotContain(envelope.Result, p => p.Id == trainerOfferId);
    }

    private async Task<Guid> CreateTrainerOfferAsync(string slug)
    {
        CreateTrainerProOfferRequest request = new(
            Slug: slug,
            DisplayName: "Trainer Pro",
            PriceCents: 49_000,
            RecurringIntervalDays: 30,
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            Currency: "RUB",
            IsHighlighted: false,
            DisplayOrder: null,
            IsActive: true);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/admin/trainer-pro/offer", request);
        response.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope.Result;
    }

    [Fact]
    public async Task Empty_list_when_no_plans()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<PlanDto>>? envelope = await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PlanDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
    }
}
