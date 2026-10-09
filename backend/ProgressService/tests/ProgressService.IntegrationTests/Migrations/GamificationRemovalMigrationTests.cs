using Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using ProgressService.Domain.Bookmarks;
using ProgressService.Domain.ContentAccess;
using ProgressService.Domain.Enrollments;
using ProgressService.Infrastructure.Postgres;
using Testcontainers.PostgreSql;

namespace ProgressService.IntegrationTests.Migrations;

public class GamificationRemovalMigrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Migrate_FreshOrExistingDatabase_PreservesLearningAndBookmarks(bool upgrade)
    {
        var options = new DbContextOptionsBuilder<ProgressDbContext>()
            .UseNpgsql(_postgres.GetConnectionString()).Options;
        await using var db = new ProgressDbContext(options);
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        if (upgrade)
        {
            await db.GetService<IMigrator>().MigrateAsync("20260706150113_AddStudentQuestionAt");
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            db.CourseEnrollments.Add(enrollment);
            db.MaterialBookmarks.Add(MaterialBookmark.Create(
                userId, courseId, BookmarkEntityReference.Of(EntityType.Material, Guid.NewGuid()).Value).Value);
            db.ContentGrants.Add(ContentGrant.Create(userId, "course", courseId, "FULL").Value);
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO progress.progress_users (user_id, username) VALUES ({userId}, 'learner');
                INSERT INTO progress.user_gamification_stats
                    (id, user_id, total_xp, current_level, created_at, updated_at)
                VALUES ({Guid.NewGuid()}, {userId}, 100, 2, now(), now());
                INSERT INTO progress.xp_awards
                    (id, user_id, enrollment_id, award_type, source_id, xp_amount, created_at)
                VALUES ({Guid.NewGuid()}, {userId}, {enrollment.Id}, 'ISSUE_APPROVED', {Guid.NewGuid()}, 20, now());
                INSERT INTO progress.level_test_attempts
                    (id, quiz_id, user_id, created_at, overall_percent, level, ai_grading_status)
                VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, {userId}, now(), 80, 'MIDDLE', 'READY');
                """);
        }

        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        foreach (string table in new[] { "xp_awards", "user_gamification_stats", "level_test_attempts", "progress_users" })
        {
            await using var command = new NpgsqlCommand("SELECT to_regclass(@table) IS NULL", connection);
            command.Parameters.AddWithValue("table", "progress." + table);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
        }

        Assert.Equal(upgrade ? 1 : 0, await db.CourseEnrollments.CountAsync());
        Assert.Equal(upgrade ? 1 : 0, await db.MaterialBookmarks.CountAsync());
        Assert.Equal(upgrade ? 1 : 0, await db.ContentGrants.CountAsync());
        Assert.Empty(await db.IssueSubmissions.ToListAsync());
        Assert.Empty(await db.QuizAttempts.ToListAsync());
    }
}
