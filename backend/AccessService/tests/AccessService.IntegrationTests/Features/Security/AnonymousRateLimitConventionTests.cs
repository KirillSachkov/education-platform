using AccessService.Core.Features.Billing.UseCases;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AccessService.IntegrationTests.Features.Security;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class AnonymousRateLimitConventionTests : AccessServiceTestsBase
{
    public AnonymousRateLimitConventionTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void Anonymous_database_reads_use_anonymous_read_policy()
    {
        string[] routes =
        [
            "/access/billing-config",
            "/access/invites/{token}/preview",
            "/access/trainer-pro/offer",
            "/access/plans/public",
            "/access/plans/by-slug/{slug}",
        ];

        IReadOnlyList<RouteEndpoint> endpoints = Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

        foreach (string route in routes)
        {
            RouteEndpoint[] matchingEndpoints = endpoints
                .Where(candidate => candidate.RoutePattern.RawText == route
                    && candidate.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
                .ToArray();

            Assert.NotEmpty(matchingEndpoints);
            Assert.All(matchingEndpoints, endpoint =>
            {
                EnableRateLimitingAttribute policy = Assert.IsType<EnableRateLimitingAttribute>(
                    endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>());
                Assert.Equal("anonymous-read", policy.PolicyName);
            });
        }
    }

    [Fact]
    public void Github_install_callback_reuses_install_rate_limit()
    {
        RouteEndpoint endpoint = Assert.Single(Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(),
            candidate =>
                candidate.RoutePattern.RawText == "/access/integrations/github/install-callback/");

        EnableRateLimitingAttribute policy = Assert.IsType<EnableRateLimitingAttribute>(
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>());
        Assert.Equal("github-app-install", policy.PolicyName);
    }

    [Fact]
    public void Tbank_webhook_has_bounded_request_body()
    {
        RouteEndpoint endpoint = Assert.Single(Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>(),
            candidate => candidate.RoutePattern.RawText == "/access/webhooks/tbank/");

        IRequestSizeLimitMetadata limit = Assert.IsAssignableFrom<IRequestSizeLimitMetadata>(
            endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>());
        Assert.Equal(TBankWebhookEndpoint.MAX_BODY_BYTES, limit.MaxRequestBodySize);
    }
}
