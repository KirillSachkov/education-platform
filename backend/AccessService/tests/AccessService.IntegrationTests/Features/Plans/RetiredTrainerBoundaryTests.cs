using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RetiredTrainerBoundaryTests(IntegrationTestsWebFactory factory) : AccessServiceTestsBase(factory)
{
    [Theory]
    [InlineData("/access/trainer-pro/offer/")]
    [InlineData("/access/admin/trainer-pro/offer/")]
    [InlineData("/access/admin/trainer-pro/revenue/")]
    public async Task Dedicated_read_routes_are_removed(string path)
    {
        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/access/trainer-pro/orders/")]
    [InlineData("/access/admin/trainer-pro/offer/")]
    public async Task Dedicated_write_routes_are_removed(string path)
    {
        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(path, new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(Factory.TBankClient.InitCalls);
    }

    [Theory]
    [InlineData("TRAINER_PRO", null)]
    [InlineData("trainer_pro", null)]
    [InlineData(null, "TRAINER_PRO")]
    [InlineData(null, "trainer_pro")]
    public async Task Course_creation_cannot_enable_retired_access(string? capability, string? offerType)
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.COURSE), Slug: "retired-capability", DisplayName: "Курс",
            ShortDescription: null, LongDescription: null, CoverFileId: null, Features: null,
            PriceCents: null, Currency: "RUB", CourseIds: [Guid.NewGuid()], DisplayOrder: 0,
            Capabilities: capability is null ? null : ["VIEW_MATERIALS", capability], OfferType: offerType);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExecuteInDbAsync(async db => Assert.Empty(await db.Plans.ToListAsync()));
    }

    [Fact]
    public async Task Course_update_cannot_enable_retired_access_or_change_saved_capabilities()
    {
        Plan plan = Plan.Create(CurrentUserId, PlanTier.COURSE, PlanSlug.Of("course-update").Value,
            PlanDisplayName.Of("Курс").Value, [Guid.NewGuid()], ["VIEW_MATERIALS"]).Value;
        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        UpdatePlanRequest request = new(null, null, null, null, null, null, null, null, null,
            Capabilities: ["VIEW_MATERIALS", "TRAINER_PRO"]);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/access/plans/{plan.Id}/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExecuteInDbAsync(async db =>
            Assert.Equal(PlanCapabilities.VIEW_MATERIALS, (await db.Plans.SingleAsync()).Capabilities));
    }
}