using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Quizzes;
using ProgressService.Domain.Users;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Gamification;

[Collection(nameof(IntegrationTestsFixture))]
public class LeaderboardEndpointsTests : ProgressServiceTestsBase
{
    public LeaderboardEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetLeaderboard_ShouldReturnRankedUsers_WithStableTieBreaker()
    {
        Guid firstUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid secondUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Guid currentUserId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        Guid fourthUserId = Guid.Parse("00000000-0000-0000-0000-000000000004");

        await SeedLeaderboardUserAsync(firstUserId, "alpha", 300, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(secondUserId, "beta", 300, new DateTime(2026, 03, 01, 9, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(currentUserId, "gamma", 200, new DateTime(2026, 03, 01, 10, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(fourthUserId, "delta", 100, new DateTime(2026, 03, 01, 11, 0, 0, DateTimeKind.Utc));

        AuthenticateAsAdmin(currentUserId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/leaderboard?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(1, item.Rank);
                Assert.Equal(firstUserId, item.UserId);
                Assert.Equal("alpha", item.Username);
                Assert.False(item.IsCurrentUser);
            },
            item =>
            {
                Assert.Equal(2, item.Rank);
                Assert.Equal(secondUserId, item.UserId);
                Assert.Equal("beta", item.Username);
                Assert.False(item.IsCurrentUser);
            },
            item =>
            {
                Assert.Equal(3, item.Rank);
                Assert.Equal(currentUserId, item.UserId);
                Assert.Equal("gamma", item.Username);
                Assert.True(item.IsCurrentUser);
            },
            item =>
            {
                Assert.Equal(4, item.Rank);
                Assert.Equal(fourthUserId, item.UserId);
                Assert.Equal("delta", item.Username);
                Assert.False(item.IsCurrentUser);
            });

        Assert.NotNull(result.CurrentUser);
        Assert.Equal(3, result.CurrentUser!.Rank);
        Assert.Equal(currentUserId, result.CurrentUser.UserId);
        Assert.True(result.CurrentUser.IsCurrentUser);
    }

    [Fact]
    public async Task GetLeaderboard_WhenCurrentUserOutsideCurrentPage_ShouldStillReturnCurrentUser()
    {
        Guid firstUserId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        Guid secondUserId = Guid.Parse("00000000-0000-0000-0000-000000000012");
        Guid thirdUserId = Guid.Parse("00000000-0000-0000-0000-000000000013");
        Guid currentUserId = Guid.Parse("00000000-0000-0000-0000-000000000014");

        await SeedLeaderboardUserAsync(firstUserId, "one", 400, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(secondUserId, "two", 300, new DateTime(2026, 03, 01, 9, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(thirdUserId, "three", 200, new DateTime(2026, 03, 01, 10, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(currentUserId, "four", 100, new DateTime(2026, 03, 01, 11, 0, 0, DateTimeKind.Utc));

        AuthenticateAsAdmin(currentUserId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/leaderboard?page=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, item => item.UserId == currentUserId);
        Assert.NotNull(result.CurrentUser);
        Assert.Equal(4, result.CurrentUser!.Rank);
        Assert.Equal(currentUserId, result.CurrentUser.UserId);
        Assert.True(result.CurrentUser.IsCurrentUser);
    }

    [Fact]
    public async Task GetLeaderboard_WhenCurrentUserHasNoXp_ShouldReturnNullCurrentUser()
    {
        Guid leaderboardUserId = Guid.Parse("00000000-0000-0000-0000-000000000021");
        Guid currentUserId = Guid.Parse("00000000-0000-0000-0000-000000000022");

        await SeedLeaderboardUserAsync(leaderboardUserId, "ranked-user", 120, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));

        AuthenticateAsAdmin(currentUserId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/leaderboard?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        Assert.Single(result.Items);
        Assert.Equal(1, result.TotalCount);
        Assert.Null(result.CurrentUser);
    }

    [Fact]
    public async Task GetLeaderboard_AnonymousUser_ShouldReturnLeaderboardWithoutCurrentUser()
    {
        Guid firstUserId = Guid.Parse("00000000-0000-0000-0000-000000000031");
        Guid secondUserId = Guid.Parse("00000000-0000-0000-0000-000000000032");

        await SeedLeaderboardUserAsync(firstUserId, "public-alpha", 300, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(secondUserId, "public-beta", 120, new DateTime(2026, 03, 01, 9, 0, 0, DateTimeKind.Utc));

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/leaderboard?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        Assert.Equal(2, result.TotalCount);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(1, item.Rank);
                Assert.Equal(firstUserId, item.UserId);
                Assert.Equal("public-alpha", item.Username);
                Assert.False(item.IsCurrentUser);
            },
            item =>
            {
                Assert.Equal(2, item.Rank);
                Assert.Equal(secondUserId, item.UserId);
                Assert.Equal("public-beta", item.Username);
                Assert.False(item.IsCurrentUser);
            });
        Assert.Null(result.CurrentUser);
    }

    [Fact]
    public async Task GetLeaderboard_WithStaleDenormLevel_ComputesLevelFromTotalXp()
    {
        // #552 — user_gamification_stats.current_level может хранить уровень по старой
        // кривой (денорм пересчитывается только на XP-award). Лидерборд обязан считать
        // уровень на чтение из total_xp: SeedLeaderboardUserAsync пишет стейлый
        // current_level=4 для всех XP >= 500 (локальный ResolveLevel — старая кривая).
        Guid levelSixUserId = Guid.Parse("00000000-0000-0000-0000-000000000041");
        Guid levelFiveUserId = Guid.Parse("00000000-0000-0000-0000-000000000042");
        Guid levelFourUserId = Guid.Parse("00000000-0000-0000-0000-000000000043");

        await SeedLeaderboardUserAsync(levelSixUserId, "stale-six", 1560, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(levelFiveUserId, "stale-five", 980, new DateTime(2026, 03, 01, 9, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(levelFourUserId, "stale-four", 670, new DateTime(2026, 03, 01, 10, 0, 0, DateTimeKind.Utc));

        AuthenticateAsAdmin(levelSixUserId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/leaderboard?page=1&pageSize=15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(levelSixUserId, item.UserId);
                Assert.Equal(1560, item.TotalXp);
                Assert.Equal(6, item.CurrentLevel);
            },
            item =>
            {
                Assert.Equal(levelFiveUserId, item.UserId);
                Assert.Equal(980, item.TotalXp);
                Assert.Equal(5, item.CurrentLevel);
            },
            item =>
            {
                Assert.Equal(levelFourUserId, item.UserId);
                Assert.Equal(670, item.TotalXp);
                Assert.Equal(4, item.CurrentLevel);
            });

        Assert.NotNull(result.CurrentUser);
        Assert.Equal(6, result.CurrentUser!.CurrentLevel);
    }

    [Fact]
    public async Task GetLeaderboard_WithCourseIdFilter_ShowsGlobalLevelNotScopedXpLevel()
    {
        // #552 — в course/author-scoped лидерборде TotalXp строки — scoped-XP,
        // а уровень должен оставаться ГЛОБАЛЬНЫМ (от ugs.total_xp, не от scoped-суммы).
        Guid authorId = Guid.Parse("00000000-0000-0000-0000-00000000e001");
        Guid courseId = Guid.NewGuid();
        Guid userId = Guid.Parse("00000000-0000-0000-0000-00000000e002");

        await SeedLeaderboardUserAsync(userId, "global-six", 1560, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));
        Guid enrollmentId = await SeedAuthorEnrollmentAsync(userId, courseId, authorId);

        // AwardXpAsync намеренно не трогает user_gamification_stats — глобальный
        // total_xp остаётся 1560 из seed'а, scoped-сумма по курсу = 100.
        await AwardXpAsync(enrollmentId, userId, amount: 100);

        AuthenticateAsAdmin(userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/leaderboard?page=1&pageSize=10&courseId={courseId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        LeaderboardUserDto row = Assert.Single(result.Items);
        Assert.Equal(100, row.TotalXp);
        Assert.Equal(6, row.CurrentLevel);

        Assert.NotNull(result.CurrentUser);
        Assert.Equal(100, result.CurrentUser!.TotalXp);
        Assert.Equal(6, result.CurrentUser.CurrentLevel);
    }

    [Fact]
    public async Task GetLeaderboard_WithAuthorIdFilter_ReturnsOnlyUsersEnrolledUnderThatAuthor()
    {
        Guid authorA = Guid.Parse("00000000-0000-0000-0000-00000000a001");
        Guid authorB = Guid.Parse("00000000-0000-0000-0000-00000000a002");

        Guid userInA = Guid.Parse("00000000-0000-0000-0000-00000000b001");
        Guid userInB = Guid.Parse("00000000-0000-0000-0000-00000000b002");
        Guid currentUser = Guid.Parse("00000000-0000-0000-0000-00000000b003");

        Guid courseA = Guid.NewGuid();
        Guid courseB = Guid.NewGuid();

        await SeedLeaderboardUserAsync(userInA, "alpha-a", totalXp: 0, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(userInB, "beta-b", totalXp: 0, new DateTime(2026, 03, 01, 9, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(currentUser, "current", totalXp: 0, new DateTime(2026, 03, 01, 10, 0, 0, DateTimeKind.Utc));

        Guid enrollmentInA = await SeedAuthorEnrollmentAsync(userInA, courseA, authorA);
        Guid enrollmentInB = await SeedAuthorEnrollmentAsync(userInB, courseB, authorB);
        Guid currentInA = await SeedAuthorEnrollmentAsync(currentUser, courseA, authorA);

        await AwardXpAsync(enrollmentInA, userInA, amount: 300);
        await AwardXpAsync(enrollmentInB, userInB, amount: 500);
        await AwardXpAsync(currentInA, currentUser, amount: 100);

        AuthenticateAsAdmin(currentUser);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/leaderboard?page=1&pageSize=10&authorId={authorA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        // Only users enrolled under authorA are included
        Assert.Equal(2, result.TotalCount);
        Assert.DoesNotContain(result.Items, i => i.UserId == userInB);
        Assert.Contains(result.Items, i => i.UserId == userInA && i.TotalXp == 300);
        Assert.Contains(result.Items, i => i.UserId == currentUser && i.TotalXp == 100);

        // Current user rank comes from the author-scoped branch as well
        Assert.NotNull(result.CurrentUser);
        Assert.Equal(currentUser, result.CurrentUser!.UserId);
        Assert.Equal(100, result.CurrentUser.TotalXp);
    }

    [Fact]
    public async Task GetLeaderboard_WithCourseIdFilter_ReturnsOnlyUsersEnrolledInThatCourse()
    {
        Guid authorId = Guid.Parse("00000000-0000-0000-0000-00000000c001");

        Guid courseA = Guid.NewGuid();
        Guid courseB = Guid.NewGuid();

        Guid userInA = Guid.Parse("00000000-0000-0000-0000-00000000d001");
        Guid userInB = Guid.Parse("00000000-0000-0000-0000-00000000d002");
        Guid userInBoth = Guid.Parse("00000000-0000-0000-0000-00000000d003");

        await SeedLeaderboardUserAsync(userInA, "only-a", totalXp: 0, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(userInB, "only-b", totalXp: 0, new DateTime(2026, 03, 01, 9, 0, 0, DateTimeKind.Utc));
        await SeedLeaderboardUserAsync(userInBoth, "both", totalXp: 0, new DateTime(2026, 03, 01, 10, 0, 0, DateTimeKind.Utc));

        Guid enrollAinA = await SeedAuthorEnrollmentAsync(userInA, courseA, authorId);
        Guid enrollBinB = await SeedAuthorEnrollmentAsync(userInB, courseB, authorId);
        Guid enrollBothInA = await SeedAuthorEnrollmentAsync(userInBoth, courseA, authorId);
        Guid enrollBothInB = await SeedAuthorEnrollmentAsync(userInBoth, courseB, authorId);

        await AwardXpAsync(enrollAinA, userInA, amount: 200);
        await AwardXpAsync(enrollBinB, userInB, amount: 500);
        await AwardXpAsync(enrollBothInA, userInBoth, amount: 150);
        await AwardXpAsync(enrollBothInB, userInBoth, amount: 300);

        AuthenticateAsAdmin(userInBoth);

        // Filter by courseA — should only see userInA (200 XP) and userInBoth (150 XP from courseA only)
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/leaderboard?page=1&pageSize=10&courseId={courseA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        Assert.Equal(2, result.TotalCount);
        Assert.DoesNotContain(result.Items, i => i.UserId == userInB);
        Assert.Collection(
            result.Items,
            item =>
            {
                Assert.Equal(1, item.Rank);
                Assert.Equal(userInA, item.UserId);
                Assert.Equal(200, item.TotalXp);
            },
            item =>
            {
                Assert.Equal(2, item.Rank);
                Assert.Equal(userInBoth, item.UserId);
                Assert.Equal(150, item.TotalXp); // Only XP from courseA
                Assert.True(item.IsCurrentUser);
            });

        Assert.NotNull(result.CurrentUser);
        Assert.Equal(userInBoth, result.CurrentUser!.UserId);
        Assert.Equal(150, result.CurrentUser.TotalXp);
    }

    [Fact]
    public async Task GetLeaderboard_IncludesGlobalActivityStats()
    {
        // #572 — лидерборд отдаёт глобальные счётчики активности участника:
        //   MaterialsCompleted (material_views.is_completed=TRUE),
        //   QuizzesCompleted   (distinct passed quiz_attempts),
        //   IssuesCompleted    (distinct COMPLETED issue_progress через enrollment).
        Guid userId = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
        Guid authorId = Guid.Parse("00000000-0000-0000-0000-0000000000f2");
        Guid courseId = Guid.NewGuid();

        await SeedLeaderboardUserAsync(userId, "achiever", 500, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));

        // 2 изученных материала + 1 silent track-view (is_completed=false → не считается).
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), completed: true);
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), completed: true);
        await SeedMaterialViewAsync(userId, Guid.NewGuid(), completed: false);

        // 2 distinct пройденных квиза (quizA — двумя passed-попытками, проверка DISTINCT) + 1 проваленный.
        Guid quizA = Guid.NewGuid();
        Guid quizB = Guid.NewGuid();
        await SeedQuizAttemptAsync(userId, quizA, passed: true);
        await SeedQuizAttemptAsync(userId, quizA, passed: true);
        await SeedQuizAttemptAsync(userId, quizB, passed: true);
        await SeedQuizAttemptAsync(userId, Guid.NewGuid(), passed: false);

        // 2 решённые задачи (COMPLETED) + 1 in-progress.
        Guid enrollmentId = await SeedAuthorEnrollmentAsync(userId, courseId, authorId);
        await SeedIssueProgressAsync(enrollmentId, IssueProgressStatus.COMPLETED);
        await SeedIssueProgressAsync(enrollmentId, IssueProgressStatus.COMPLETED);
        await SeedIssueProgressAsync(enrollmentId, IssueProgressStatus.IN_PROGRESS);

        AuthenticateAsAdmin(userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/leaderboard?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        LeaderboardUserDto row = Assert.Single(result.Items);
        Assert.Equal(2, row.MaterialsCompleted);
        Assert.Equal(2, row.QuizzesCompleted);
        Assert.Equal(2, row.IssuesCompleted);

        // Карточка «Ваше место» (current-user) тоже обогащена теми же счётчиками.
        Assert.NotNull(result.CurrentUser);
        Assert.Equal(2, result.CurrentUser!.MaterialsCompleted);
        Assert.Equal(2, result.CurrentUser.QuizzesCompleted);
        Assert.Equal(2, result.CurrentUser.IssuesCompleted);
    }

    [Fact]
    public async Task GetLeaderboard_WithoutActivity_ReturnsZeroStats()
    {
        Guid userId = Guid.Parse("00000000-0000-0000-0000-0000000000f8");
        await SeedLeaderboardUserAsync(userId, "idle", 120, new DateTime(2026, 03, 01, 8, 0, 0, DateTimeKind.Utc));

        AuthenticateAsAdmin(userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/leaderboard?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetLeaderboardResponse result = await ReadWrappedResultAsync<GetLeaderboardResponse>(response);

        LeaderboardUserDto row = Assert.Single(result.Items);
        Assert.Equal(0, row.MaterialsCompleted);
        Assert.Equal(0, row.QuizzesCompleted);
        Assert.Equal(0, row.IssuesCompleted);
    }

    private async Task SeedMaterialViewAsync(Guid userId, Guid materialId, bool completed)
    {
        await ExecuteInDb(async db =>
        {
            MaterialView view = completed
                ? MaterialView.CreateCompleted(userId, materialId).Value
                : MaterialView.CreateTrack(userId, materialId).Value;
            db.MaterialViews.Add(view);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedQuizAttemptAsync(Guid userId, Guid quizId, bool passed)
    {
        await ExecuteInDb(async db =>
        {
            QuizAttempt attempt = QuizAttempt.Create(
                userId, quizId, [], passed ? 100 : 0, passed).Value;
            db.QuizAttempts.Add(attempt);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedIssueProgressAsync(Guid enrollmentId, IssueProgressStatus status)
    {
        await ExecuteInDb(async db =>
        {
            IssueProgress progress = IssueProgress.Create(enrollmentId, Guid.NewGuid(), Guid.NewGuid()).Value;
            db.IssueProgresses.Add(progress);
            await db.SaveChangesAsync();

            if (status != IssueProgressStatus.NOT_STARTED)
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE issue_progress SET status = {status.ToString()} WHERE id = {progress.Id}");
            }
        });
    }

    private async Task<Guid> SeedAuthorEnrollmentAsync(Guid userId, Guid courseId, Guid authorId)
    {
        return await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(userId, courseId, authorId, EnrollmentSource.ENGAGEMENT).Value;
            db.CourseEnrollments.Add(enrollment);
            await db.SaveChangesAsync();
            return enrollment.Id;
        });
    }

    private async Task AwardXpAsync(Guid enrollmentId, Guid userId, int amount)
    {
        // Insert a single XpAward directly — the per-author leaderboard SQL aggregates
        // SUM(xa.xp_amount) JOIN course_enrollments, so this is enough to exercise the branch.
        await ExecuteInDb(async db =>
        {
            XpAward award = XpAward.Create(
                userId,
                enrollmentId,
                XpAwardType.ISSUE_APPROVED,
                Guid.NewGuid(),
                amount).Value;
            db.XpAwards.Add(award);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedLeaderboardUserAsync(Guid userId, string? username, int totalXp, DateTime updatedAt)
    {
        await ExecuteInDb(async dbContext =>
        {
            var userResult = ProgressUser.Create(userId, username);
            dbContext.ProgressUsers.Add(userResult.Value);

            var statsResult = UserGamificationStats.Create(userId, 1);
            statsResult.Value.AddXp(totalXp, ResolveLevel(totalXp));
            dbContext.UserGamificationStats.Add(statsResult.Value);

            await dbContext.SaveChangesAsync();

            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE user_gamification_stats
                 SET updated_at = {updatedAt}
                 WHERE user_id = {userId}
                 """);
        });
    }

    private static int ResolveLevel(int totalXp) =>
        totalXp switch
        {
            >= 500 => 4,
            >= 250 => 3,
            >= 100 => 2,
            _ => 1
        };
}
