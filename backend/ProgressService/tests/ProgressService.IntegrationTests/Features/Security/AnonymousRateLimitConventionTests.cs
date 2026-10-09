using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ProgressService.Core.Features.Courses.Queries;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Security;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class AnonymousRateLimitConventionTests : ProgressServiceTestsBase
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
            "/progress/courses/{courseId:guid}/public-stats",
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
                Assert.Equal(GetCoursePublicStatsEndpoint.ANONYMOUS_READ_RATE_LIMIT_POLICY, policy.PolicyName);
            });
        }
    }
}