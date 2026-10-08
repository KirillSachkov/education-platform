using AssignmentReviewService.Core;
using AssignmentReviewService.Core.Features.Reviews;
using AssignmentReviewService.Core.Maintenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AssignmentReviewService.UnitTests.Configuration;

public sealed class OptionsValidationTests
{
    [Fact]
    public void Zero_diff_batch_size_should_fail_validation()
    {
        ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AssignmentReviewAI:Limits:MaxDiffAdditions"] = "0",
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AssignmentReviewAiOptions>>().Value);
    }

    [Fact]
    public void Hard_diff_cap_below_batch_size_should_fail_validation()
    {
        ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AssignmentReviewAI:Limits:MaxDiffAdditions"] = "2000",
            ["AssignmentReviewAI:Limits:HardMaxDiffAdditions"] = "1000",
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<AssignmentReviewAiOptions>>().Value);
    }

    [Fact]
    public void Zero_dead_letter_interval_should_fail_validation()
    {
        ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AssignmentReviewMaintenance:DeadLetterCleanup:Interval"] = "00:00:00",
        });

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<DeadLetterCleanupOptions>>().Value);
    }

    [Fact]
    public void Minus_one_author_limit_should_keep_unlimited_configuration_valid()
    {
        ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            ["AssignmentReviewAI:Limits:MaxIterationsPerAuthorPerDay"] = "-1",
        });

        AssignmentReviewAiOptions options =
            provider.GetRequiredService<IOptions<AssignmentReviewAiOptions>>().Value;

        Assert.Equal(-1, options.Limits.MaxIterationsPerAuthorPerDay);
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
