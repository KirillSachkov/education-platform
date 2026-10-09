using System.Net;
using System.Net.Http.Json;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.Campaigns;

public class RetiredLevelTestCampaignTests : NotificationServiceTestsBase
{
    public RetiredLevelTestCampaignTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Run_RetiredCampaign_ReturnsNotFound()
    {
        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/notifications/admin/campaigns/level-test-invite/run/", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}