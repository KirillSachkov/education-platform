using System.Net;
using System.Net.Http.Headers;
using PlatformAuth.Authorization;
using SearchService.IntegrationTests.Infrastructure;

namespace SearchService.IntegrationTests.Features.Reindex;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ReindexAuthorizationTests : SearchServiceTestsBase
{
    public ReindexAuthorizationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Full_reindex_endpoint_should_require_authenticated_user()
    {
        HttpResponseMessage response = await AppHttpClient.PostAsync("/internal/reindex", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Full_reindex_endpoint_should_forbid_user_without_required_role()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/internal/reindex");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SCHEME_NAME,
            $"{Guid.NewGuid()}|Participant|participant@test.local|{PlatformRoles.PARTICIPANT}");

        HttpResponseMessage response = await AppHttpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_reindex_endpoint_should_require_authenticated_user()
    {
        HttpResponseMessage response = await AppHttpClient.PostAsync("/search/admin/reindex", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_reindex_endpoint_should_forbid_non_admin()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/search/admin/reindex");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SCHEME_NAME,
            $"{Guid.NewGuid()}|Participant|participant@test.local|{PlatformRoles.PARTICIPANT}");

        HttpResponseMessage response = await AppHttpClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_reindex_endpoint_should_accept_admin()
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/search/admin/reindex");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            TestAuthHandler.SCHEME_NAME,
            $"{Guid.NewGuid()}|Admin|admin@test.local|{PlatformRoles.ADMIN}");

        HttpResponseMessage response = await AppHttpClient.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.Accepted,
            $"Expected 200/202, got {(int)response.StatusCode} {response.StatusCode}");
    }
}
