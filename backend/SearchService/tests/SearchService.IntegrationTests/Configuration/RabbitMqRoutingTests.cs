using SearchService.Core.Messaging;
using Shared.Messaging.IntegrationEvents.Education;

namespace SearchService.IntegrationTests.Configuration;

public sealed class RabbitMqRoutingTests
{
    [Fact]
    public void Education_queue_should_bind_only_events_with_search_handlers()
    {
        string[] routingKeys = RabbitMqConfiguration.EducationLifecycleRoutingKeys.ToArray();

        Assert.Equal(36, routingKeys.Length);
        Assert.Equal(routingKeys.Length, routingKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(routingKeys, static key => key.Contains('*', StringComparison.Ordinal));
        Assert.Contains(EducationEventsRouting.RoutingKeys.IssueAccessChanged(), routingKeys);
    }
}
