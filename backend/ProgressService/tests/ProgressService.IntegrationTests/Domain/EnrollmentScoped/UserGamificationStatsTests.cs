using ProgressService.Domain.Gamification;

namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

public class UserGamificationStatsTests
{
    [Fact]
    public void AddXp_ShouldIncreaseTotalXp_AndUpdateLevel()
    {
        UserGamificationStats stats = UserGamificationStats.Create(Guid.NewGuid(), 1).Value;

        var result = stats.AddXp(120, 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(120, stats.TotalXp);
        Assert.Equal(2, stats.CurrentLevel);
    }

    [Fact]
    public void AddXp_WhenLevelDecreases_ShouldFail()
    {
        UserGamificationStats stats = UserGamificationStats.Create(Guid.NewGuid(), 2).Value;

        var result = stats.AddXp(10, 1);

        Assert.True(result.IsFailure);
        Assert.Equal(0, stats.TotalXp);
        Assert.Equal(2, stats.CurrentLevel);
    }
}
