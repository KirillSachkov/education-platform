using System.Net;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Health;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class HealthEndpointTests : TagServiceTestsBase
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
