using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Responses;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Activity;

[Collection(nameof(IntegrationTestsFixture))]
public class GetMyActivityTests : ProgressServiceTestsBase
{
    private const string ACTIVITY_URL = "/progress/my/activity";

    public GetMyActivityTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyActivity_WhenAnonymous_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(ACTIVITY_URL);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyActivity_WhenNoActivity_ShouldReturnZeroFilledDays()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(ACTIVITY_URL);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MyActivityResponse dto = await ReadWrappedResultAsync<MyActivityResponse>(response);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(84, dto.Days.Count);
        Assert.Equal(today.AddDays(-83), dto.Days[0].Date);
        Assert.Equal(today, dto.Days[^1].Date);
        Assert.All(dto.Days, day =>
        {
            Assert.Equal(0, day.Xp);
            Assert.Equal(0, day.MaterialsCompleted);
        });

        Assert.Equal(0, dto.Streak.Current);
        Assert.Equal(0, dto.Streak.Longest);
        Assert.Equal(0, dto.Totals.TotalXp);
        Assert.Equal(0, dto.Totals.MaterialsCompleted);
        Assert.Equal(0, dto.Totals.IssuesApproved);
    }

    [Fact]
    public async Task GetMyActivity_WithSeededActivity_ShouldReturnPerDayAggregatesStreakAndTotals()
    {
        Guid userId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        // Сегодня: материал изучен + XP за просмотр.
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: now, completedAtUtc: now);
        await SeedXpAwardAsync(userId, now, xpAmount: 10, awardType: "MATERIAL_VIEWED");

        // Вчера: задача принята + материал изучен.
        DateTime yesterday = now.AddDays(-1);
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: yesterday, completedAtUtc: yesterday);
        await SeedXpAwardAsync(userId, yesterday, xpAmount: 20, awardType: "ISSUE_APPROVED");

        // 3 дня назад: только материал изучен (день активности без XP).
        DateTime threeDaysAgo = now.AddDays(-3);
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: threeDaysAgo, completedAtUtc: threeDaysAgo);

        // 5 дней назад: silent track-view — день активности, но не «изучено».
        DateTime fiveDaysAgo = now.AddDays(-5);
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: fiveDaysAgo, completedAtUtc: null);

        // Чужая активность не должна попадать в ответ.
        await SeedMaterialViewAsync(otherUserId, Guid.NewGuid(), viewedAtUtc: now, completedAtUtc: now);
        await SeedXpAwardAsync(otherUserId, now, xpAmount: 100, awardType: "ISSUE_APPROVED");

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(ACTIVITY_URL);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MyActivityResponse dto = await ReadWrappedResultAsync<MyActivityResponse>(response);

        Assert.Equal(84, dto.Days.Count);

        ActivityDayDto todayDay = dto.Days[^1];
        Assert.Equal(10, todayDay.Xp);
        Assert.Equal(1, todayDay.MaterialsCompleted);

        ActivityDayDto yesterdayDay = dto.Days[^2];
        Assert.Equal(20, yesterdayDay.Xp);
        Assert.Equal(1, yesterdayDay.MaterialsCompleted);

        ActivityDayDto threeDaysAgoDay = dto.Days[^4];
        Assert.Equal(0, threeDaysAgoDay.Xp);
        Assert.Equal(1, threeDaysAgoDay.MaterialsCompleted);

        // Silent track-view — не «изучено», нули в дневной сетке.
        ActivityDayDto fiveDaysAgoDay = dto.Days[^6];
        Assert.Equal(0, fiveDaysAgoDay.Xp);
        Assert.Equal(0, fiveDaysAgoDay.MaterialsCompleted);

        // Дни активности: −5, −3, −1, 0 → текущая серия 2 (сегодня + вчера, разрыв на −2).
        Assert.Equal(2, dto.Streak.Current);
        Assert.Equal(2, dto.Streak.Longest);

        Assert.Equal(30, dto.Totals.TotalXp);
        Assert.Equal(3, dto.Totals.MaterialsCompleted);
        Assert.Equal(1, dto.Totals.IssuesApproved);
    }

    [Fact]
    public async Task GetMyActivity_LongestStreak_ShouldBeComputedOverFullHistoryBeyondWindow()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        // Серия из 3 дней за пределами 84-дневного окна (100..98 дней назад).
        await SeedXpAwardAsync(userId, now.AddDays(-100), xpAmount: 10, awardType: "MATERIAL_VIEWED");
        await SeedXpAwardAsync(userId, now.AddDays(-99), xpAmount: 10, awardType: "MODULE_COMPLETED");
        await SeedXpAwardAsync(userId, now.AddDays(-98), xpAmount: 10, awardType: "PROJECT_COMPLETED");

        // Сегодня: одиночный день активности.
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: now, completedAtUtc: now);

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(ACTIVITY_URL);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MyActivityResponse dto = await ReadWrappedResultAsync<MyActivityResponse>(response);

        // Стрик считается по всей истории, окно дней — только последние 84.
        Assert.Equal(1, dto.Streak.Current);
        Assert.Equal(3, dto.Streak.Longest);

        Assert.Equal(84, dto.Days.Count);
        Assert.Equal(0, dto.Days[0].Xp);

        // XP вне окна виден только в итогах.
        Assert.Equal(30, dto.Totals.TotalXp);
        Assert.Equal(1, dto.Totals.MaterialsCompleted);
        Assert.Equal(0, dto.Totals.IssuesApproved);
    }

    [Fact]
    public async Task GetMyActivity_WhenLastActivityYesterday_ShouldKeepCurrentStreak()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        // Активность вчера и позавчера, сегодня — пока нет: серия не рвётся до завтра.
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: now.AddDays(-2), completedAtUtc: now.AddDays(-2));
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: now.AddDays(-1), completedAtUtc: now.AddDays(-1));

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(ACTIVITY_URL);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MyActivityResponse dto = await ReadWrappedResultAsync<MyActivityResponse>(response);

        Assert.Equal(2, dto.Streak.Current);
        Assert.Equal(2, dto.Streak.Longest);
    }

    [Fact]
    public async Task GetMyActivity_WhenLastActivityTwoDaysAgo_ShouldResetCurrentStreak()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        await SeedMaterialViewAsync(userId, Guid.NewGuid(), viewedAtUtc: now.AddDays(-2), completedAtUtc: now.AddDays(-2));

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(ACTIVITY_URL);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        MyActivityResponse dto = await ReadWrappedResultAsync<MyActivityResponse>(response);

        Assert.Equal(0, dto.Streak.Current);
        Assert.Equal(1, dto.Streak.Longest);
    }

    /// <summary>
    /// Сидит запись xp_awards с контролируемой датой начисления. Domain-фабрика
    /// фиксирует <c>CreatedAt = UtcNow</c>, поэтому сидим raw SQL'ем
    /// (прецедент — ReviewWorkflowEndpointsTests).
    /// </summary>
    private Task SeedXpAwardAsync(Guid userId, DateTime createdAtUtc, int xpAmount, string awardType)
    {
        return ExecuteInDb(db => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO xp_awards (id, user_id, enrollment_id, award_type, source_id, xp_amount, created_at)
            VALUES ({0}, {1}, NULL, {2}, {3}, {4}, {5})
            """,
            Guid.NewGuid(), userId, awardType, Guid.NewGuid(), xpAmount, createdAtUtc));
    }

    /// <summary>
    /// Сидит material_views с контролируемыми датами. <paramref name="completedAtUtc"/> = null —
    /// silent track-view (день активности без «изучено»).
    /// </summary>
    private Task SeedMaterialViewAsync(Guid userId, Guid materialId, DateTime viewedAtUtc, DateTime? completedAtUtc)
    {
        if (completedAtUtc is null)
        {
            return ExecuteInDb(db => db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO material_views (id, user_id, material_id, viewed_at, created_at, is_completed, completed_at)
                VALUES ({0}, {1}, {2}, {3}, {3}, FALSE, NULL)
                """,
                Guid.NewGuid(), userId, materialId, viewedAtUtc));
        }

        return ExecuteInDb(db => db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO material_views (id, user_id, material_id, viewed_at, created_at, is_completed, completed_at)
            VALUES ({0}, {1}, {2}, {3}, {3}, TRUE, {4})
            """,
            Guid.NewGuid(), userId, materialId, viewedAtUtc, completedAtUtc.Value));
    }
}
