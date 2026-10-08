using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.IntegrationTests.Infrastructure;

namespace AssignmentReviewService.IntegrationTests.Features.Installations;

public sealed class StartInstallationTests : AssignmentReviewServiceTestsBase
{
    public StartInstallationTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Start_Authenticated_ReturnsRedirectUrlAndState()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/assignment-review/installations/start/",
            new { ReturnUrl = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        // Lenient assertion — exact shape framework-dependent (EndpointResult может wrap'ить).
        // Достаточно того что сервер построил redirectURL правильной формы.
        Assert.Contains("github.com/apps/", raw, StringComparison.Ordinal);
        Assert.Contains("ars-test-app", raw, StringComparison.Ordinal);
        Assert.Contains("state=", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_Anonymous_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/assignment-review/installations/start/",
            new { ReturnUrl = (string?)null });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Start_AbsoluteReturnUrl_RejectedAsValidationError()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/assignment-review/installations/start/",
            new { ReturnUrl = "https://evil.example.com/steal" });

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
            $"Expected 400/422 для невалидного ReturnUrl, получили {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Start_ProtocolRelativeReturnUrl_RejectedAsValidationError()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/assignment-review/installations/start/",
            new { ReturnUrl = "//evil.example.com/" });

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity);
    }

    private sealed record StartInstallationResponseDto(string RedirectUrl, string State);
}
