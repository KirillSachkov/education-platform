using System.Diagnostics.CodeAnalysis;
using FileService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PlatformDatabase;
using Testcontainers.PostgreSql;

namespace FileService.IntegrationTests.Features.Videos;

public sealed class TranscriptPreservationMigrationTests
{
    private const string PREVIOUS_MIGRATION = "20260710224504_AddAssetOwnershipCheckpoint";

    [Fact]
    public async Task BrokenLegacySource_FailsAndRollsBackEntireMigration()
    {
        await using PostgreSqlContainer container = CreateContainer();
        await container.StartAsync();
        await using FileServiceDbContext db = CreateContext(container.GetConnectionString());
        await PrepareRequiredReadSchemasAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(PREVIOUS_MIGRATION);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA material_processing;
            CREATE TABLE material_processing.video_transcripts (id uuid PRIMARY KEY);
            INSERT INTO material_processing.video_transcripts VALUES ('01900000-0000-7000-8000-000000000001');
            """);

        await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());

        await using var connection = new NpgsqlConnection(container.GetConnectionString().WithPlatformDefaults());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT to_regclass('files.video_transcripts') IS NULL
                AND (SELECT count(*) FROM material_processing.video_transcripts) = 1
                AND NOT EXISTS (SELECT 1 FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" = '20261009080000_PreserveCompletedVideoTranscripts')
            """, connection);
        Assert.True((bool)(await command.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task FreshDatabase_HasRetainedArtifactStorageWithoutLegacySchema()
    {
        await using PostgreSqlContainer container = CreateContainer();
        await container.StartAsync();
        await using FileServiceDbContext db = CreateContext(container.GetConnectionString());
        await PrepareRequiredReadSchemasAsync(db);

        await db.Database.MigrateAsync();

        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Equal("[]", await ReadRowsAsync(container.GetConnectionString(), "files"));
    }

    [Fact]
    public async Task Upgrade_AndSyntheticArchiveRestore_PreserveEveryRawRowAndVersion()
    {
        await using PostgreSqlContainer container = CreateContainer();
        await container.StartAsync();
        string connectionString = container.GetConnectionString();
        await using FileServiceDbContext db = CreateContext(connectionString);
        await PrepareRequiredReadSchemasAsync(db);
        await db.GetService<IMigrator>().MigrateAsync(PREVIOUS_MIGRATION);
        await using var sourceConnection = new NpgsqlConnection(connectionString.WithPlatformDefaults());
        await sourceConnection.OpenAsync();
        await using var sourceCommand = new NpgsqlCommand("""
            CREATE SCHEMA material_processing;
            CREATE TABLE material_processing.video_transcripts (
                id uuid PRIMARY KEY, video_asset_id uuid NOT NULL, asset_version uuid NOT NULL,
                duration_seconds integer NOT NULL, language varchar(16) NOT NULL,
                segments_json jsonb NOT NULL, created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                UNIQUE(video_asset_id, asset_version)
            );
            INSERT INTO material_processing.video_transcripts VALUES
                ('01900000-0000-7000-8000-000000000001', '01900000-0000-7000-8000-000000000011',
                 '01900000-0000-7000-8000-000000000021', 3605, 'ru',
                 '[{"start":3600.125,"end":3602.75,"text":"Синтетический архив\nвторая строка","extra":"preserve"}]',
                 '2026-01-02T03:04:05.123456Z', '2026-02-03T04:05:06.654321Z'),
                ('01900000-0000-7000-8000-000000000002', '01900000-0000-7000-8000-000000000011',
                 '01900000-0000-7000-8000-000000000022', 9, 'en',
                 '[{"startSeconds":5,"endSeconds":9,"text":"Synthetic current version"},{"startSeconds":0,"endSeconds":1,"text":"Keep original order"}]',
                 '2026-03-04T05:06:07Z', '2026-04-05T06:07:08Z');
            """, sourceConnection);
        await sourceCommand.ExecuteNonQueryAsync();
        // No media assets exist: both rows are orphans, one is an older version.
        string original = await ReadRowsAsync(connectionString, "material_processing");

        await db.Database.MigrateAsync();

        Assert.Equal(original, await ReadRowsAsync(connectionString, "files"));
        Assert.Equal(original, await ReadRowsAsync(connectionString, "material_processing"));
        Assert.False(db.Database.HasPendingModelChanges());
        // Archive/restore uses only the synthetic rows above, inside this disposable container.
        var dump = await container.ExecAsync(["pg_dump", "-U", "postgres", "-d", "postgres", "-Fc",
            "-n", "material_processing", "-f", "/tmp/synthetic-transcripts.dump"]);
        Assert.Equal(0, dump.ExitCode);
        var create = await container.ExecAsync(["createdb", "-U", "postgres", "synthetic_restore"]);
        Assert.Equal(0, create.ExitCode);
        var restore = await container.ExecAsync(["pg_restore", "-U", "postgres", "-d", "synthetic_restore",
            "--exit-on-error", "/tmp/synthetic-transcripts.dump"]);
        Assert.Equal(0, restore.ExitCode);
        var restored = new NpgsqlConnectionStringBuilder(connectionString) { Database = "synthetic_restore" };
        Assert.Equal(original, await ReadRowsAsync(restored.ConnectionString, "material_processing"));
        await using FileServiceDbContext restoredDb = CreateContext(restored.ConnectionString);
        await PrepareRequiredReadSchemasAsync(restoredDb);
        await restoredDb.Database.MigrateAsync();
        Assert.Equal(original, await ReadRowsAsync(restored.ConnectionString, "files"));

        // The retained read storage survives loss of its legacy source in a test database.
        await db.Database.ExecuteSqlRawAsync("DROP SCHEMA material_processing CASCADE;");
        Assert.Equal(original, await ReadRowsAsync(connectionString, "files"));
        await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(PREVIOUS_MIGRATION));
        Assert.Equal(original, await ReadRowsAsync(connectionString, "files"));
    }

    private static PostgreSqlContainer CreateContainer() => new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("postgres").WithUsername("postgres").WithPassword("postgres").Build();

    private static FileServiceDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<FileServiceDbContext>();
        options.UsePlatformNpgsql(connectionString);
        return new FileServiceDbContext(options.Options);
    }

    private static Task PrepareRequiredReadSchemasAsync(FileServiceDbContext db) =>
        // Existing FileService migrations read these authoritative schemas to backfill bindings.
        // Use the same empty stubs as its integration factory; no MPS schema is created here.
        db.Database.ExecuteSqlRawAsync("""
            CREATE SCHEMA auth;
            CREATE SCHEMA education;
            CREATE TABLE auth.user_profiles (id uuid PRIMARY KEY, avatar_id uuid NULL);
            CREATE TABLE education.materials (id uuid PRIMARY KEY, image_id uuid NULL, video_id uuid NULL);
            CREATE TABLE education.courses (id uuid PRIMARY KEY, image_id uuid NULL, video_id uuid NULL);
            CREATE TABLE education.collections (id uuid PRIMARY KEY, cover_image_id uuid NULL);
            """);

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Schema identifiers are constants selected exclusively by these tests, never user input.")]
    private static async Task<string> ReadRowsAsync(string connectionString, string schema)
    {
        // schema is selected exclusively by these tests, never by request input.
        await using var connection = new NpgsqlConnection(connectionString.WithPlatformDefaults());
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY id), '[]'::jsonb)::text FROM {schema}.video_transcripts t", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}