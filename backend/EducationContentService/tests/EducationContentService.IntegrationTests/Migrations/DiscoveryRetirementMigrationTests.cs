using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Ordering;
using PlatformDatabase;
using Testcontainers.PostgreSql;

namespace EducationContentService.IntegrationTests.Migrations;

public sealed class DiscoveryRetirementMigrationTests
{
    private static readonly string[] _retiredTables = ["roadmaps", "roadmap_nodes", "roadmap_edges", "short_links"];
    private static readonly string[] _retainedTables = ["courses", "materials", "course_materials", "collections", "collection_sections", "collection_items"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retirement_SupportsFreshDatabaseAndPreservesExistingCourseContent(bool upgrade)
    {
        await using PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await postgres.StartAsync();
        string connectionString = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString())
        {
            SearchPath = "education,public",
        }.ConnectionString;
        DbContextOptionsBuilder<EducationDbContext> options = new();
        options.UsePlatformNpgsql(connectionString);
        await using EducationDbContext db = new(options.Options);
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS education; CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;");
        Dictionary<string, string[]> before = [];

        if (upgrade)
        {
            string[] migrations = db.Database.GetMigrations().ToArray();
            string retirement = Assert.Single(migrations, name => name.EndsWith("_RetireRoadmapsAndShortLinks", StringComparison.Ordinal));
            int index = Array.IndexOf(migrations, retirement);
            Assert.True(index > 0);
            await db.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
            await SeedRetainedContentAsync(db);
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

    private static async Task SeedRetainedContentAsync(EducationDbContext db)
    {
        Guid authorId = Guid.CreateVersion7();
        SortKey sortKey = SortKey.Create("a0").Value;
        Course course = new(authorId, Title.Create("Retained course").Value,
            Description.Create("Existing course description").Value,
            CourseSlug.Create("retained-course").Value, sortKey);
        Material material = new(authorId, Title.Create("Retained lesson").Value,
            accessType: EducationContentService.Domain.AccessType.ENROLLED);
        material.SetContent(MarkdownContent.Create("# Existing lesson body").Value);
        Material legacyArticle = new(authorId, Title.Create("Legacy unbound article").Value);
        legacyArticle.SetContent(MarkdownContent.Create("# Existing article body").Value);
        Collection collection = new(authorId, Title.Create("Course collection").Value, course.Id);
        CollectionSection section = new(collection.Id, "Retained section", null, sortKey);
        CollectionItem item = new(section.Id, CollectionItemType.MATERIAL, material.Id, sortKey);
        db.Courses.Add(course);
        db.Materials.AddRange(material, legacyArticle);
        db.CourseMaterials.Add(new CourseMaterial(course.Id, material.Id, sortKey));
        db.Collections.Add(collection);
        db.CollectionSections.Add(section);
        db.CollectionItems.Add(item);
        await db.SaveChangesAsync();
        Guid roadmapId = Guid.CreateVersion7();
        Guid firstNodeId = Guid.CreateVersion7();
        Guid secondNodeId = Guid.CreateVersion7();
        string nodeData = "{\"entityType\":\"Material\",\"entityId\":\"" + material.Id + "\"}";
        int inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO education.roadmaps (id, author_id, title, course_id, slug, status)
            VALUES ({roadmapId}, {authorId}, 'Retired roadmap', {course.Id}, 'retired-roadmap', 'PUBLISHED');
            INSERT INTO education.roadmap_nodes (id, roadmap_id, node_type, position_x, position_y, data, sort_order)
            VALUES ({firstNodeId}, {roadmapId}, 'EntityReference', 0, 0, {nodeData}::jsonb, 0),
                   ({secondNodeId}, {roadmapId}, 'EntityReference', 100, 0, {nodeData}::jsonb, 1);
            INSERT INTO education.roadmap_edges (id, roadmap_id, source_node_id, target_node_id, edge_type, animated)
            VALUES ({Guid.CreateVersion7()}, {roadmapId}, {firstNodeId}, {secondNodeId}, 'DEFAULT', false);
            INSERT INTO education.short_links (id, material_id, code, created_at)
            VALUES ({Guid.CreateVersion7()}, {material.Id}, 'retired-code', {DateTime.UtcNow});
            """);
        Assert.Equal(5, inserted);
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using NpgsqlCommand command = new("SELECT to_regclass(@name)::text", connection);
        command.Parameters.AddWithValue("name", "education." + table);
        return await command.ExecuteScalarAsync() is string;
    }

    private static async Task<Dictionary<string, string[]>> ReadRetainedStateAsync(NpgsqlConnection connection)
    {
        const string sql = """
            SELECT 'courses' AS table_name, to_jsonb(entry)::text AS body FROM education.courses entry
            UNION ALL
            SELECT 'materials' AS table_name, to_jsonb(entry)::text AS body FROM education.materials entry
            UNION ALL
            SELECT 'course_materials' AS table_name, to_jsonb(entry)::text AS body FROM education.course_materials entry
            UNION ALL
            SELECT 'collections' AS table_name, to_jsonb(entry)::text AS body FROM education.collections entry
            UNION ALL
            SELECT 'collection_sections' AS table_name, to_jsonb(entry)::text AS body FROM education.collection_sections entry
            UNION ALL
            SELECT 'collection_items' AS table_name, to_jsonb(entry)::text AS body FROM education.collection_items entry
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