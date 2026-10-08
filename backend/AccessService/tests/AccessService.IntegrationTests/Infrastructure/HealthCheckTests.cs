namespace AccessService.IntegrationTests.Infrastructure;

public class HealthCheckTests : AccessServiceTestsBase
{
    public HealthCheckTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        // Anonymous read — health endpoints don't require auth.
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body, StringComparison.Ordinal);
    }
}
