using Microsoft.Extensions.Options;
using ProgressService.Core.Configuration;
using ProgressService.Core.Services;

namespace ProgressService.IntegrationTests.Services;

public class XpLevelPolicyTests
{
    private readonly XpLevelPolicy _policy = new(
        Options.Create(new GamificationOptions
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
                new GamificationLevelOptions { Level = 3, RequiredXp = 250 },
                new GamificationLevelOptions { Level = 4, RequiredXp = 500 }
            ]
        }));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(499, 3)]
    [InlineData(500, 4)]
    public void ResolveLevel_ShouldReturnExpectedLevel(int totalXp, int expectedLevel)
    {
        int level = _policy.ResolveLevel(totalXp);

        Assert.Equal(expectedLevel, level);
    }

    [Fact]
    public void ResolveNextLevel_WhenMaxLevelReached_ShouldReturnNull()
    {
        int? nextLevel = _policy.ResolveNextLevel(500);

        Assert.Null(nextLevel);
    }

    [Fact]
    public void ResolveXpToNextLevel_ShouldReturnRemainingXp()
    {
        int? xpToNextLevel = _policy.ResolveXpToNextLevel(60);

        Assert.Equal(40, xpToNextLevel);
    }
}
