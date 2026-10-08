using System.Net;
using EducationContentService.IntegrationTests.Infrastructure;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class HealthEndpointTests : EducationContentServiceTestsBase
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
