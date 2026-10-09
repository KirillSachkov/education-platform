using System.Net;
using System.Net.Http.Json;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Security;

[Collection(nameof(IntegrationTestsFixture))]
public class RetiredFeatureEndpointsTests : ProgressServiceTestsBase
{
    public RetiredFeatureEndpointsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Theory]
    [InlineData("/progress/me/gamification/")]
    [InlineData("/progress/leaderboard/")]
    [InlineData("/progress/my/activity/")]
    [InlineData("/progress/level-test/attempts/my-latest/")]
    public async Task Get_RetiredFeature_ReturnsNotFound(string path)
    {
        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        HttpResponseMessage response = await AppHttpClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Submit_RetiredLevelTest_ReturnsNotFound()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/progress/level-test/attempts/", new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}