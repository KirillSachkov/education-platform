using System.Net;
using FileService.IntegrationTests.Infrastructure;

namespace FileService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class HealthEndpointTests : FileServiceTestsBase
{
    public HealthEndpointTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
