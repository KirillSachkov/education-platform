using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Gamification;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Gamification;

[Collection(nameof(IntegrationTestsFixture))]
public class GamificationEndpointsTests : ProgressServiceTestsBase
{
    public GamificationEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyXpProgress_WhenStatsMissing_ShouldReturnDefaults()
    {
        Guid userId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/me/gamification");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserXpProgressResponse dto = await ReadWrappedResultAsync<UserXpProgressResponse>(response);

        Assert.Equal(0, dto.TotalXp);
        Assert.Equal(1, dto.CurrentLevel);
        Assert.Equal(2, dto.NextLevel);
        Assert.Equal(100, dto.XpToNextLevel);
    }

    [Fact]
    public async Task GetMyXpProgress_WhenStatsExist_ShouldReturnResolvedProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddModule(courseId, moduleId, 1);
        EducationContentClient.AddMaterial(moduleId, lessonId, 1);
        EducationContentClient.AddMaterialCourseContext(lessonId, courseId, moduleId, moduleItemsTotal: 1);

        await SeedEnrollmentAsync(courseId, userId);
        await PostAsync($"/progress/courses/{courseId}/modules/{moduleId}/start");
        await PostAsync($"/progress/materials/{lessonId}/view");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/me/gamification");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserXpProgressResponse dto = await ReadWrappedResultAsync<UserXpProgressResponse>(response);

        Assert.Equal(60, dto.TotalXp);
        Assert.Equal(1, dto.CurrentLevel);
        Assert.Equal(2, dto.NextLevel);
        Assert.Equal(40, dto.XpToNextLevel);

        int awardsCount = await ExecuteInDb(dbContext =>
            dbContext.XpAwards.CountAsync(x => x.UserId == userId));

        Assert.Equal(2, awardsCount);
    }

    // Регрессия #509: кривая уровней обрывалась на Level 4 (500 XP) — все активные
    // пользователи навсегда застревали на 4-м уровне с nextLevel=null, хотя XP рос.
    [Fact]
    public async Task GetMyXpProgress_WhenXpAboveOldLevel4Cap_ShouldReturnNonNullNextLevel()
    {
        Guid userId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        await SeedUserStatsAsync(userId, totalXp: 600);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/me/gamification");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserXpProgressResponse dto = await ReadWrappedResultAsync<UserXpProgressResponse>(response);

        Assert.Equal(600, dto.TotalXp);
        Assert.Equal(4, dto.CurrentLevel);
        Assert.Equal(5, dto.NextLevel);
        Assert.Equal(300, dto.XpToNextLevel);
    }

    [Fact]
    public async Task GetMyXpProgress_WhenXpAboveLevel5Threshold_ShouldReturnLevel5()
    {
        Guid userId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        await SeedUserStatsAsync(userId, totalXp: 950);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/me/gamification");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserXpProgressResponse dto = await ReadWrappedResultAsync<UserXpProgressResponse>(response);

        Assert.Equal(950, dto.TotalXp);
        Assert.Equal(5, dto.CurrentLevel);
        Assert.Equal(6, dto.NextLevel);
        Assert.Equal(550, dto.XpToNextLevel);
    }

    [Fact]
    public async Task GetMyXpProgress_WhenXpExactlyAtLevel5Threshold_ShouldReturnLevel5()
    {
        Guid userId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        await SeedUserStatsAsync(userId, totalXp: 900);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/me/gamification");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserXpProgressResponse dto = await ReadWrappedResultAsync<UserXpProgressResponse>(response);

        Assert.Equal(900, dto.TotalXp);
        Assert.Equal(5, dto.CurrentLevel);
        Assert.Equal(6, dto.NextLevel);
        Assert.Equal(600, dto.XpToNextLevel);
    }

    /// <summary>
    /// Сидит агрегат статистики с нужным total_xp напрямую в БД. Персистентный
    /// current_level намеренно остаётся стейлым (=1): read-path обязан вычислять
    /// уровень из конфига по total_xp, а не доверять денорм-колонке.
    /// </summary>
    private async Task SeedUserStatsAsync(Guid userId, int totalXp)
    {
        await ExecuteInDb(async db =>
        {
            UserGamificationStats stats = UserGamificationStats.Create(userId, initialLevel: 1).Value;
            stats.AddXp(totalXp, newLevel: 1);

            await db.UserGamificationStats.AddAsync(stats);
            await db.SaveChangesAsync();
        });
    }
}
