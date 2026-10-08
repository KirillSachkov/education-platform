using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Security;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class EndpointAuthorizationConventionTests : ProgressServiceTestsBase
{
    public EndpointAuthorizationConventionTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void Every_progress_endpoint_declares_authorization_or_anonymous_access()
    {
        IReadOnlyList<RouteEndpoint> unprotectedEndpoints = Services
            .GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/progress", StringComparison.Ordinal) == true)
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .Where(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count == 0)
            .ToArray();

        Assert.Empty(unprotectedEndpoints);
    }
}
