using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using TagService.Infrastructure.Postgres;
using TagService.Web.Configuration;

namespace TagService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Test-only SQL is built exclusively from generated database identifiers and constant statements.")]
public sealed class TagMigrationHistoryReconciliationCliTests
{
    private const string SquashedInitMigration = "20260412192249_Init";
    private const string EntityTagIndexMigration = "20260413150009_AddEntityTagUniqueIndex";
    private const string TagSlugScopeMigration = "20260413181605_FixTagSlugAuthorScope";
    private const string SquashedBaselineMigration = "20260422162948_DropTagKindDefault";
    private const string AuthorConstraintMigration = "20260712012702_EnforceTagAuthorId";
    private const string AliasUniquenessMigration = "20260712013310_EnforceSingleCanonicalAlias";

    private readonly IntegrationTestsWebFactory _factory;

    public TagMigrationHistoryReconciliationCliTests(IntegrationTestsWebFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RunAsync_ReconcilesProductionSquashDriftBeforeApplyingPendingMigrations()
    {
        await WithTemporaryDatabaseAsync(async connectionString =>
        {
            await MigrateAsync(connectionString, SquashedBaselineMigration);
            await ExecuteSqlAsync(connectionString, """
                DROP INDEX tags."IX_tag_aliases_alias_tag_id";
                ALTER TABLE tags.tags
                    ADD CONSTRAINT ck_tags_author_id_not_empty
                    CHECK (author_id <> '00000000-0000-0000-0000-000000000000');

                INSERT INTO tags.tags (id, author_id, title, slug, kind)
                VALUES
                    ('00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-000000000010', 'Canonical 1', 'canonical-1', 'CANON'),
                    ('00000000-0000-7000-8000-000000000002', '00000000-0000-7000-8000-000000000010', 'Canonical 2', 'canonical-2', 'CANON'),
                    ('00000000-0000-7000-8000-000000000003', '00000000-0000-7000-8000-000000000010', 'Alias', 'alias', 'ALIAS');

                INSERT INTO tags.tag_aliases (id, tag_id, alias_tag_id)
                VALUES
                    ('00000000-0000-7000-8000-000000000011', '00000000-0000-7000-8000-000000000001', '00000000-0000-7000-8000-000000000003'),
                    ('00000000-0000-7000-8000-000000000012', '00000000-0000-7000-8000-000000000002', '00000000-0000-7000-8000-000000000003');
                """);

            await RunReconciliationAsync(connectionString);
            await MigrateAsync(connectionString);

            await AssertLatestSchemaAsync(connectionString);
            Assert.Equal(1, await ExecuteScalarAsync<long>(connectionString,
                "SELECT COUNT(*) FROM tags.tag_aliases;"));
        });
    }

    [Theory]
    [InlineData(MigrationState.EmptyDatabase)]
    [InlineData(MigrationState.SquashedBaseline)]
    [InlineData(MigrationState.FirstJulyMigrationApplied)]
    [InlineData(MigrationState.Latest)]
    public async Task RunAsync_IsIdempotentAcrossReachableMigrationStates(MigrationState state)
    {
        await WithTemporaryDatabaseAsync(async connectionString =>
        {
            await ArrangeStateAsync(connectionString, state);

            await RunReconciliationAsync(connectionString);
            await RunReconciliationAsync(connectionString);
            await MigrateAsync(connectionString);

            await AssertLatestSchemaAsync(connectionString);
        });
    }

    [Fact]
    public async Task RunAsync_FailsClosedWhenExistingConstraintHasUnexpectedDefinition()
    {
        await WithTemporaryDatabaseAsync(async connectionString =>
        {
            await MigrateAsync(connectionString, SquashedBaselineMigration);
            await ExecuteSqlAsync(connectionString, """
                ALTER TABLE tags.tags
                    ADD CONSTRAINT ck_tags_author_id_not_empty
                    CHECK (author_id IS NOT NULL);
                """);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunReconciliationAsync(connectionString));

            Assert.Contains("unexpected definition", exception.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task RunAsync_FailsClosedWhenTagTablesExistWithoutMigrationHistory()
    {
        await WithTemporaryDatabaseAsync(async connectionString =>
        {
            await MigrateAsync(connectionString, SquashedBaselineMigration);
            await ExecuteSqlAsync(connectionString, "DROP TABLE tags.\"__EFMigrationsHistory\";");

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunReconciliationAsync(connectionString));

            Assert.Contains("migration state cannot be reconciled safely", exception.Message,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData("CREATE SCHEMA tags; CREATE TABLE tags.tags (id uuid);")]
    [InlineData("CREATE SCHEMA tags; CREATE TABLE tags.tag_aliases (id uuid);")]
    public async Task RunAsync_FailsClosedWhenOnlyOneCoreTableExists(string arrangeSql)
    {
        await WithTemporaryDatabaseAsync(async connectionString =>
        {
            await ExecuteSqlAsync(connectionString, arrangeSql);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunReconciliationAsync(connectionString));

            Assert.Contains("partial TagService schema", exception.Message,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task RunAsync_FailsClosedWhenMigrationHistoryExistsWithoutCoreTables()
    {
        await WithTemporaryDatabaseAsync(async connectionString =>
        {
            await MigrateAsync(connectionString);
            await ExecuteSqlAsync(connectionString, """
                DROP TABLE tags.tag_aliases;
                DROP TABLE tags.tags CASCADE;
                """);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunReconciliationAsync(connectionString));

            Assert.Contains("core tables are missing", exception.Message,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    [Theory]
    [InlineData(SquashedInitMigration)]
    [InlineData(EntityTagIndexMigration)]
    [InlineData(TagSlugScopeMigration)]
    [InlineData(SquashedBaselineMigration)]
    public async Task RunAsync_FailsClosedWhenCoreTablesExistWithoutRequiredBaselineHistory(
        string missingMigrationId)
    {
        await WithTemporaryDatabaseAsync(async connectionString =>
        {
            await MigrateAsync(connectionString, SquashedBaselineMigration);
            await ExecuteSqlAsync(connectionString, $"""
                DELETE FROM tags."__EFMigrationsHistory"
                WHERE "MigrationId" = '{missingMigrationId}';
                """);

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => RunReconciliationAsync(connectionString));

            Assert.Contains(missingMigrationId, exception.Message, StringComparison.Ordinal);
        });
    }

    private static async Task ArrangeStateAsync(string connectionString, MigrationState state)
    {
        switch (state)
        {
            case MigrationState.EmptyDatabase:
                return;
            case MigrationState.SquashedBaseline:
                await MigrateAsync(connectionString, SquashedBaselineMigration);
                return;
            case MigrationState.FirstJulyMigrationApplied:
                await MigrateAsync(connectionString, AuthorConstraintMigration);
                await ExecuteSqlAsync(connectionString,
                    "DROP INDEX tags.\"IX_tag_aliases_alias_tag_id\";");
                return;
            case MigrationState.Latest:
                await MigrateAsync(connectionString);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }

    private static async Task RunReconciliationAsync(string connectionString)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = connectionString,
            })
            .Build();

        await TagMigrationHistoryReconciliationCli.RunAsync(configuration);
    }

    private static async Task MigrateAsync(string connectionString, string? targetMigration = null)
    {
        DbContextOptions<TagDbContext> options = new DbContextOptionsBuilder<TagDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tags"))
            .Options;

        await using var dbContext = new TagDbContext(options);
        IMigrator migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    private static async Task AssertLatestSchemaAsync(string connectionString)
    {
        string[] migrationIds = await QueryStringsAsync(connectionString, """
            SELECT "MigrationId"
            FROM tags."__EFMigrationsHistory"
            WHERE "MigrationId" IN (
                '20260712012702_EnforceTagAuthorId',
                '20260712013310_EnforceSingleCanonicalAlias')
            ORDER BY "MigrationId";
            """);
        Assert.Equal([AuthorConstraintMigration, AliasUniquenessMigration], migrationIds);

        string? constraintDefinition = await ExecuteScalarAsync<string>(connectionString, """
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conrelid = 'tags.tags'::regclass
              AND conname = 'ck_tags_author_id_not_empty';
            """);
        Assert.Equal(
            "CHECK ((author_id <> '00000000-0000-0000-0000-000000000000'::uuid))",
            constraintDefinition);

        string? indexDefinition = await ExecuteScalarAsync<string>(connectionString, """
            SELECT pg_get_indexdef('tags.ux_tag_aliases_alias_tag_id'::regclass);
            """);
        Assert.Equal(
            "CREATE UNIQUE INDEX ux_tag_aliases_alias_tag_id ON tags.tag_aliases USING btree (alias_tag_id)",
            indexDefinition);

        string? legacyIndex = await ExecuteScalarAsync<string>(connectionString,
            "SELECT to_regclass('tags.\"IX_tag_aliases_alias_tag_id\"')::text;");
        Assert.Null(legacyIndex);
    }

    private async Task WithTemporaryDatabaseAsync(Func<string, Task> action)
    {
        string databaseName = $"tag_migration_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(_factory.DatabaseConnectionString)
        {
            Database = "postgres",
            SearchPath = null,
        };

        await using (var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConnection.OpenAsync();
            await using NpgsqlCommand create = adminConnection.CreateCommand();
            create.CommandText = $"CREATE DATABASE {QuoteIdentifier(databaseName)};";
            await create.ExecuteNonQueryAsync();
        }

        var databaseBuilder = new NpgsqlConnectionStringBuilder(_factory.DatabaseConnectionString)
        {
            Database = databaseName,
            SearchPath = "tags,public",
        };

        try
        {
            await action(databaseBuilder.ConnectionString);
        }
        finally
        {
            await using (var pooledConnection = new NpgsqlConnection(databaseBuilder.ConnectionString))
                NpgsqlConnection.ClearPool(pooledConnection);

            await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await adminConnection.OpenAsync();
            await using NpgsqlCommand drop = adminConnection.CreateCommand();
            drop.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(databaseName)} WITH (FORCE);";
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteSqlAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T?> ExecuteScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? result = await command.ExecuteScalarAsync();
        if (result is null || result is DBNull)
            return default;
        return (T)result;
    }

    private static async Task<string[]> QueryStringsAsync(string connectionString, string sql)
    {
        var result = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(reader.GetString(0));
        return result.ToArray();
    }

    private static string QuoteIdentifier(string identifier) =>
        '"' + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';

    public enum MigrationState
    {
        EmptyDatabase,
        SquashedBaseline,
        FirstJulyMigrationApplied,
        Latest,
    }
}
