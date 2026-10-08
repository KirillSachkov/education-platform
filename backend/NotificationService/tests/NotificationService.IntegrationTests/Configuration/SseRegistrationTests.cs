using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Web;

namespace NotificationService.IntegrationTests.Configuration;

public sealed class SseRegistrationTests
{
    [Fact]
    public void Redis_fanout_without_redis_connection_should_fail_registration()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Sse:RedisFanoutEnabled"] = "true",
                ["AccessServiceOptions:Url"] = "http://access-service:8010",
                ["EducationServiceOptions:Url"] = "http://education-service:8001",
                ["AuthServiceOptions:Url"] = "http://auth-service:8005",
                ["ConnectionStrings:Database"] = "Host=localhost;Database=notifications",
            })
            .Build();
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddNotificationServiceRegistrations(configuration));
    }
}
