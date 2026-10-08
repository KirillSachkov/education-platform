using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ArchivePlanTests : AccessServiceTestsBase
{
    public ArchivePlanTests(IntegrationTestsWebFactory factory) : base(factory) { }

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
    public async Task Author_archives_own_plan()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.NotNull(plan.ArchivedAt);
            Assert.False(plan.IsActive);
            Assert.False(plan.IsPublic);
        });
    }

    [Fact]
    public async Task Already_archived_plan_returns_validation_error()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        first.EnsureSuccessStatusCode();

        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        Envelope? envelope = await second.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "plan.archived", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Foreign_author_cannot_archive_someone_elses_plan()
    {
        Guid planId = await CreatePlanAsync();

        Guid foreignUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", foreignUserId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Author_unarchives_own_plan()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage archive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        archive.EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/unarchive", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Null(plan.ArchivedAt);
            Assert.True(plan.IsActive);
            // IsPublic is NOT restored — Unarchive doesn't republish; author must explicitly publish again
            Assert.False(plan.IsPublic);
        });
    }

    [Fact]
    public async Task Unarchive_is_idempotent_for_active_plan()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/unarchive", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Null(plan.ArchivedAt);
            Assert.True(plan.IsActive);
        });
    }
}
