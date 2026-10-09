using Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PlatformDatabase;
using ProgressService.Domain.Bookmarks;
using ProgressService.Domain.ContentAccess;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Modules;
using ProgressService.Infrastructure.Postgres;
using Testcontainers.PostgreSql;

namespace ProgressService.IntegrationTests.Migrations;

public sealed class DiscoveryRetirementMigrationTests
{
    private static readonly string[] _retiredTables = ["material_notes", "course_certificates"];
    private static readonly string[] _retainedTables = ["content_grants", "course_enrollments", "material_bookmarks", "material_views", "module_progress", "module_item_progress"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_SupportsFreshDatabaseAndPreservesBookmarksAndProgress(bool upgrade)
    {
        await using PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        string connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            SearchPath = "progress,public",
        }.ConnectionString;
        DbContextOptionsBuilder<ProgressDbContext> options = new();
        options.UsePlatformNpgsql(connectionString);
        await using ProgressDbContext db = new(options.Options);
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS progress;");
        Dictionary<string, string[]> before = [];

        if (upgrade)
        {
            string[] migrations = db.Database.GetMigrations().ToArray();
            string retirement = Assert.Single(migrations, name => name.EndsWith("_RetireCertificatesAndMaterialNotes", StringComparison.Ordinal));
            int index = Array.IndexOf(migrations, retirement);
            Assert.True(index > 0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
            await SeedRetainedProgressAsync(db);
            foreach (string table in _retiredTables)
                Assert.True(await TableExistsAsync(connection, table));
            before = await ReadRetainedStateAsync(connection);
            foreach (string table in _retainedTables)
                Assert.NotEmpty(before[table]);
        }

        await db.Database.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());

        foreach (string table in _retiredTables)
            Assert.False(await TableExistsAsync(connection, table));
        Dictionary<string, string[]> after = await ReadRetainedStateAsync(connection);
        foreach (string table in _retainedTables)
        {
            Assert.True(await TableExistsAsync(connection, table));
            if (upgrade)
                Assert.Equal(before[table], after[table]);
        }
    }

    private static async Task SeedRetainedProgressAsync(ProgressDbContext db)
    {
        Guid userId = Guid.CreateVersion7();
        Guid courseId = Guid.CreateVersion7();
        Guid moduleId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(userId, courseId,
            Guid.CreateVersion7(), EnrollmentSource.ENGAGEMENT).Value;
        MaterialBookmark bookmark = MaterialBookmark.Create(userId, courseId,
            BookmarkEntityReference.Of(EntityType.Material, materialId).Value).Value;
        MaterialBookmark issueBookmark = MaterialBookmark.Create(userId, courseId,
            BookmarkEntityReference.Of(EntityType.Issue, Guid.CreateVersion7()).Value).Value;
        ModuleProgress moduleProgress = ModuleProgress.Create(enrollment.Id, moduleId, 1).Value;
        moduleProgress.MarkItemCompleted();
        ModuleItemProgress itemProgress = ModuleItemProgress.CreateMaterialProgress(enrollment.Id, moduleId, materialId).Value;
        itemProgress.MarkCompleted();
        db.ContentGrants.Add(ContentGrant.Create(userId, "Course", courseId, "PURCHASE",
            DateTime.UtcNow.AddDays(30)).Value);
        db.CourseEnrollments.Add(enrollment);
        db.MaterialBookmarks.AddRange(bookmark, issueBookmark);
        db.MaterialViews.Add(MaterialView.CreateCompleted(userId, materialId).Value);
        db.ModuleProgresses.Add(moduleProgress);
        db.ModuleItemProgresses.Add(itemProgress);
        await db.SaveChangesAsync();
        DateTime now = DateTime.UtcNow;
        int inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO progress.material_notes (id, user_id, material_id, content, created_at, updated_at)
            VALUES ({Guid.CreateVersion7()}, {userId}, {materialId}, 'Retired note', {now}, {now});
            INSERT INTO progress.course_certificates (id, user_id, course_id, serial_number, course_title, holder_name, issued_at)
            VALUES ({Guid.CreateVersion7()}, {userId}, {courseId}, 'retired-certificate', 'Retained course', 'Retained learner', {now});
            """);
        Assert.Equal(2, inserted);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using NpgsqlCommand command = new("SELECT to_regclass(@name)::text", connection);
        command.Parameters.AddWithValue("name", "progress." + table);
        return await command.ExecuteScalarAsync() is string;
    }

    private static async Task<Dictionary<string, string[]>> ReadRetainedStateAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT 'content_grants' AS table_name, to_jsonb(entry)::text AS body FROM progress.content_grants entry
            UNION ALL
            SELECT 'course_enrollments' AS table_name, to_jsonb(entry)::text AS body FROM progress.course_enrollments entry
            UNION ALL
            SELECT 'material_bookmarks' AS table_name, to_jsonb(entry)::text AS body FROM progress.material_bookmarks entry
            UNION ALL
            SELECT 'material_views' AS table_name, to_jsonb(entry)::text AS body FROM progress.material_views entry
            UNION ALL
            SELECT 'module_progress' AS table_name, to_jsonb(entry)::text AS body FROM progress.module_progress entry
            UNION ALL
            SELECT 'module_item_progress' AS table_name, to_jsonb(entry)::text AS body FROM progress.module_item_progress entry
            ORDER BY table_name, body;
            """;
        await using NpgsqlCommand command = new(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        Dictionary<string, List<string>> rows = _retainedTables.ToDictionary(table => table, _ => new List<string>());
        while (await reader.ReadAsync())
            rows[reader.GetString(0)].Add(reader.GetString(1));
        return rows.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}