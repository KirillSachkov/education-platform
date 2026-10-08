using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class PublishPlanTests : AccessServiceTestsBase
{
    public PublishPlanTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreatePlanAsync(string slug = "full-access")
    {
        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Полный доступ",
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
    public async Task Author_publishes_own_plan()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.True(plan.IsPublic);
        });
    }

    [Fact]
    public async Task Author_unpublishes_own_plan()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/unpublish", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.False(plan.IsPublic);
        });
    }

    [Fact]
    public async Task Foreign_author_cannot_publish_someone_elses_plan()
    {
        Guid planId = await CreatePlanAsync();

        Guid foreignUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", foreignUserId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_is_idempotent()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        first.EnsureSuccessStatusCode();

        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Envelope<Guid>? envelope = await second.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.True(plan.IsPublic);
        });
    }

    [Fact]
    public async Task Second_full_access_plan_on_platform_cannot_be_published()
    {
        Guid firstPlanId = await CreatePlanAsync("full-access-a");
        HttpResponseMessage publishFirst = await AppHttpClient.PostAsync(
            $"/access/plans/{firstPlanId}/publish", content: null);
        publishFirst.EnsureSuccessStatusCode();

        AuthenticateAs("platform-author", Guid.NewGuid());
        Guid secondPlanId = await CreatePlanAsync("full-access-b");

        HttpResponseMessage publishSecond = await AppHttpClient.PostAsync(
            $"/access/plans/{secondPlanId}/publish", content: null);

        Assert.Equal(HttpStatusCode.Conflict, publishSecond.StatusCode);

        Envelope? envelope = await publishSecond.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "plan.tier.duplicate", StringComparison.Ordinal));
    }
}
