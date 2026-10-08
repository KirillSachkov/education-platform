using System.Net;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SearchService.Web.Configuration;

namespace SearchService.IntegrationTests.Configuration;

public sealed class SearchRateLimitingTests
{
    [Fact]
    public void Search_public_policy_uses_name_identifier_for_authenticated_users()
    {
        Guid userId = Guid.NewGuid();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                authenticationType: "test")),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.10.10.10");

        object policy = GetSearchPublicPolicy();
        object partitionKey = GetPartitionKey(policy, context);

        Assert.Equal($"user:{userId}", partitionKey);
    }

    private static object GetSearchPublicPolicy()
    {
        using ServiceProvider serviceProvider = new ServiceCollection()
            .AddSearchRateLimiting()
            .BuildServiceProvider();

        RateLimiterOptions options = serviceProvider
            .GetRequiredService<IOptions<RateLimiterOptions>>()
            .Value;

        PropertyInfo policyMapProperty = typeof(RateLimiterOptions).GetProperty(
            "PolicyMap",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RateLimiterOptions.PolicyMap was not found.");

        object policyMap = policyMapProperty.GetValue(options)
            ?? throw new InvalidOperationException("RateLimiterOptions.PolicyMap was null.");

        object policy = policyMap
            .GetType()
            .GetProperty("Item")!
            .GetValue(policyMap, [SearchRateLimiting.SEARCH_PUBLIC_POLICY])!;

        return policy;
    }

    private static object GetPartitionKey(object policy, HttpContext context)
    {
        MethodInfo getPartitionMethod = policy.GetType().GetMethod(
            "GetPartition",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Rate limiter policy GetPartition method was not found.");

        object partition = getPartitionMethod.Invoke(policy, [context])
            ?? throw new InvalidOperationException("Rate limiter policy returned null partition.");

        PropertyInfo partitionKeyProperty = partition.GetType().GetProperty("PartitionKey")
            ?? throw new InvalidOperationException("RateLimitPartition.PartitionKey was not found.");

        object partitionKey = partitionKeyProperty.GetValue(partition)
            ?? throw new InvalidOperationException("RateLimitPartition.PartitionKey was null.");

        PropertyInfo? wrappedKeyProperty = partitionKey.GetType().GetProperty(
            "Key",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        return wrappedKeyProperty?.GetValue(partitionKey) ?? partitionKey;
    }
}
