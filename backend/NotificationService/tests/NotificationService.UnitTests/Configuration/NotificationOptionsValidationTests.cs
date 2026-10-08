using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Core.Channels.WebPush;

namespace NotificationService.UnitTests.Configuration;

public sealed class NotificationOptionsValidationTests
{
    [Fact]
    public void Invalid_retention_interval_should_fail_options_validation()
    {
        ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notifications:Retention:IntervalHours"] = "0",
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<NotificationOptions>>().Value);
    }

    [Fact]
    public void Invalid_digest_schedule_should_fail_options_validation()
    {
        ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notifications:Digest:HourUtc"] = "24",
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<NotificationOptions>>().Value);
    }

    [Fact]
    public void Partial_web_push_keypair_should_fail_options_validation()
    {
        ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Notifications:WebPush:PublicKey"] = "public-key-only",
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<WebPushOptions>>().Value);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddCore(configuration);
        return services.BuildServiceProvider();
    }
}
