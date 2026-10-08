using System.Net;
using AuthService.IntegrationTests.Infrastructure;

namespace AuthService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestFixture))]
public class PublicEndpointsTests : IntegrationTestsBase
{
    public PublicEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Health_AnonymousRequest_ShouldReturnOk()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_AnonymousRequest_ShouldReturnOk()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
