namespace AssignmentReviewService.IntegrationTests.Infrastructure;

public sealed class HealthCheckTests : AssignmentReviewServiceTestsBase
{
    public HealthCheckTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Health_endpoint_returns_healthy()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync("/health");

        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body, StringComparison.Ordinal);
    }
}
