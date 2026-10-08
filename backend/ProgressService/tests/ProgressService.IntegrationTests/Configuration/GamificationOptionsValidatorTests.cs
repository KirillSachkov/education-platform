using ProgressService.Core.Configuration;

namespace ProgressService.IntegrationTests.Configuration;

public class GamificationOptionsValidatorTests
{
    private readonly GamificationOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenOptionsAreValid_ShouldSucceed()
    {
        GamificationOptions options = CreateValidOptions();

        var result = _validator.Validate(name: null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenFirstLevelDoesNotStartFromZeroXp_ShouldFail()
    {
        GamificationOptions options = CreateValidOptions();
        options.Levels[0] = new GamificationLevelOptions { Level = 1, RequiredXp = 10 };

        var result = _validator.Validate(name: null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, x => x.Contains("RequiredXp = 0", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_WhenLevelThresholdsAreNotStrictlyIncreasing_ShouldFail()
    {
        GamificationOptions options = CreateValidOptions();
        options.Levels[1] = new GamificationLevelOptions { Level = 2, RequiredXp = 0 };

        var result = _validator.Validate(name: null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Contains(result.Failures!, x => x.Contains("strictly increasing", StringComparison.Ordinal));
    }

    private static GamificationOptions CreateValidOptions() =>
        new()
        {
            Awards = new GamificationAwardsOptions
            {
                IssueApproved = 20,
                ModuleCompleted = 50,
                ProjectCompleted = 100,
                MaterialViewed = 10
            },
            Levels =
            [
                new GamificationLevelOptions { Level = 1, RequiredXp = 0 },
                new GamificationLevelOptions { Level = 2, RequiredXp = 100 },
                new GamificationLevelOptions { Level = 3, RequiredXp = 250 }
            ]
        };
}
