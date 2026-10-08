using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Stats;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.Snapshots;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/stats/trends (#681 T6): student «vs месяц назад» per-topic mastery comparison from
///     <c>topic_mastery_snapshots</c>. Covers happy-path per-topic deltas + overall, graceful null-then for
///     topics without a month-ago snapshot, overall=null when nothing is comparable, the empty branch (no
///     today snapshot anchor), current-user scoping (no cross-user leak), deleted-topic title fallback, and
///     auth (anonymous → 401).
/// </summary>
public sealed class TrendStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    private static DateOnly MonthAgo => Today.AddDays(-GetTrendsComparisonDays);
    private const int GetTrendsComparisonDays = 30;

    [Fact]
    public async Task Anonymous_gets_401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await Client.GetAsync("/trainer/stats/trends");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Trends_are_empty_when_user_has_no_today_snapshot()
    {
        AuthenticateAs("platform-participant");

        // Only a month-ago snapshot exists → no "now" anchor → graceful empty (no half-populated compare).
        await SeedMasterySnapshotAsync(MonthAgo, CurrentUserId, Guid.NewGuid(), 50);

        TrainerTrendsDto dto = await ReadResultAsync<TrainerTrendsDto>(
            await Client.GetAsync("/trainer/stats/trends"));

        Assert.Equal(30, dto.ComparisonDays);
        Assert.Empty(dto.Topics);
        Assert.Null(dto.OverallMasteryNow);
        Assert.Null(dto.OverallMasteryThen);
        Assert.Null(dto.OverallDelta);
    }

    [Fact]
    public async Task Trends_compare_today_vs_month_ago_per_topic_and_overall()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicA = await CreateTopicAsync(trackId, "trend-a", "Тема A");
        Guid topicB = await CreateTopicAsync(trackId, "trend-b", "Тема B");

        AuthenticateAs("platform-participant"); // same DefaultUserId — owns the snapshots below

        // topicA improved (+20), topicB regressed (-20).
        await SeedMasterySnapshotAsync(Today, CurrentUserId, topicA, 70);
        await SeedMasterySnapshotAsync(MonthAgo, CurrentUserId, topicA, 50);
        await SeedMasterySnapshotAsync(Today, CurrentUserId, topicB, 40);
        await SeedMasterySnapshotAsync(MonthAgo, CurrentUserId, topicB, 60);

        TrainerTrendsDto dto = await ReadResultAsync<TrainerTrendsDto>(
            await Client.GetAsync("/trainer/stats/trends"));

        Assert.Equal(2, dto.Topics.Count);

        // Biggest improvement first.
        TopicMasteryTrendDto a = dto.Topics[0];
        Assert.Equal(topicA, a.TopicId);
        Assert.Equal("Тема A", a.TopicTitle);
        Assert.Equal(70, a.MasteryNow);
        Assert.Equal(50, a.MasteryThen);
        Assert.Equal(20, a.Delta);

        TopicMasteryTrendDto b = dto.Topics[1];
        Assert.Equal(topicB, b.TopicId);
        Assert.Equal(40, b.MasteryNow);
        Assert.Equal(60, b.MasteryThen);
        Assert.Equal(-20, b.Delta);

        // Overall over the comparable set (both): now=(70+40)/2=55, then=(50+60)/2=55, delta=0.
        Assert.Equal(55, dto.OverallMasteryNow);
        Assert.Equal(55, dto.OverallMasteryThen);
        Assert.Equal(0, dto.OverallDelta);
    }

    [Fact]
    public async Task Topic_without_month_ago_snapshot_gets_null_then_and_sorts_last()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid withHistory = await CreateTopicAsync(trackId, "with-hist", "С историей");
        Guid newTopic = await CreateTopicAsync(trackId, "new-topic", "Новая");

        AuthenticateAs("platform-participant");
        await SeedMasterySnapshotAsync(Today, CurrentUserId, withHistory, 80);
        await SeedMasterySnapshotAsync(MonthAgo, CurrentUserId, withHistory, 60);
        await SeedMasterySnapshotAsync(Today, CurrentUserId, newTopic, 30); // no month-ago point

        TrainerTrendsDto dto = await ReadResultAsync<TrainerTrendsDto>(
            await Client.GetAsync("/trainer/stats/trends"));

        Assert.Equal(2, dto.Topics.Count);

        TopicMasteryTrendDto hist = Assert.Single(dto.Topics, t => t.TopicId == withHistory);
        Assert.Equal(60, hist.MasteryThen);
        Assert.Equal(20, hist.Delta);

        TopicMasteryTrendDto fresh = Assert.Single(dto.Topics, t => t.TopicId == newTopic);
        Assert.Equal(30, fresh.MasteryNow);
        Assert.Null(fresh.MasteryThen);
        Assert.Null(fresh.Delta);

        // Topic without history sorts after the comparable one.
        Assert.Equal(newTopic, dto.Topics[^1].TopicId);

        // Overall counts only the comparable topic.
        Assert.Equal(80, dto.OverallMasteryNow);
        Assert.Equal(60, dto.OverallMasteryThen);
        Assert.Equal(20, dto.OverallDelta);
    }

    [Fact]
    public async Task Overall_is_null_when_no_topic_has_history()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topic = await CreateTopicAsync(trackId, "only-now", "Только сейчас");

        AuthenticateAs("platform-participant");
        await SeedMasterySnapshotAsync(Today, CurrentUserId, topic, 45); // today only

        TrainerTrendsDto dto = await ReadResultAsync<TrainerTrendsDto>(
            await Client.GetAsync("/trainer/stats/trends"));

        TopicMasteryTrendDto row = Assert.Single(dto.Topics);
        Assert.Equal(45, row.MasteryNow);
        Assert.Null(row.MasteryThen);
        Assert.Null(row.Delta);

        Assert.Null(dto.OverallMasteryNow);
        Assert.Null(dto.OverallMasteryThen);
        Assert.Null(dto.OverallDelta);
    }

    [Fact]
    public async Task Trends_are_scoped_to_the_current_user()
    {
        Guid otherUser = Guid.NewGuid();

        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid foreignTopic = await CreateTopicAsync(trackId, "foreign", "Чужая");
        Guid myTopic = await CreateTopicAsync(trackId, "mine", "Моя");

        // Another user has a strong trend on foreignTopic — must NOT bleed into the caller's result.
        await SeedMasterySnapshotAsync(Today, otherUser, foreignTopic, 90);
        await SeedMasterySnapshotAsync(MonthAgo, otherUser, foreignTopic, 10);

        AuthenticateAs("platform-participant"); // DefaultUserId
        await SeedMasterySnapshotAsync(Today, CurrentUserId, myTopic, 55);
        await SeedMasterySnapshotAsync(MonthAgo, CurrentUserId, myTopic, 50);

        TrainerTrendsDto dto = await ReadResultAsync<TrainerTrendsDto>(
            await Client.GetAsync("/trainer/stats/trends"));

        TopicMasteryTrendDto row = Assert.Single(dto.Topics);
        Assert.Equal(myTopic, row.TopicId);
        Assert.Equal(55, row.MasteryNow);
        Assert.Equal(5, row.Delta);
        Assert.DoesNotContain(dto.Topics, t => t.TopicId == foreignTopic);
        Assert.Equal(5, dto.OverallDelta);
    }

    [Fact]
    public async Task Deleted_topic_falls_back_to_default_title()
    {
        AuthenticateAs("platform-participant");

        Guid orphanTopicId = Guid.NewGuid(); // no topic row ever created
        await SeedMasterySnapshotAsync(Today, CurrentUserId, orphanTopicId, 60);
        await SeedMasterySnapshotAsync(MonthAgo, CurrentUserId, orphanTopicId, 40);

        TrainerTrendsDto dto = await ReadResultAsync<TrainerTrendsDto>(
            await Client.GetAsync("/trainer/stats/trends"));

        TopicMasteryTrendDto row = Assert.Single(dto.Topics);
        Assert.Equal("Тема", row.TopicTitle);
        Assert.Equal(20, row.Delta);
    }

    // --- helpers ---

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug, string title)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, title, "Описание", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task SeedMasterySnapshotAsync(DateOnly date, Guid userId, Guid topicId, double mastery) =>
        await ExecuteInDbAsync(async db =>
        {
            db.TopicMasterySnapshots.Add(TopicMasterySnapshot.Create(date, userId, topicId, mastery));
            return await db.SaveChangesAsync();
        });
}
