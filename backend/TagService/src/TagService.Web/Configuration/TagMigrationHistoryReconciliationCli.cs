using System.Data;
using System.Diagnostics.CodeAnalysis;
using Npgsql;
using Serilog;

namespace TagService.Web.Configuration;

/// <summary>
///     Reconciles the TagService migration history after the April 2026 migration squash.
///     The pre-squash production schema contains objects that are absent from the squashed
///     baseline, so the immutable July migrations cannot always be applied directly.
/// </summary>
[SuppressMessage(
    "Security",
    "CA2100:Review SQL queries for security vulnerabilities",
    Justification = "Private SQL helpers receive only constant statements declared in this class; values are parameterized.")]
public static class TagMigrationHistoryReconciliationCli
{
    public const string COMMAND_NAME = "reconcile-migration-history";

    private const string SQUASHED_INIT_MIGRATION = "20260412192249_Init";
    private const string ENTITY_TAG_INDEX_MIGRATION = "20260413150009_AddEntityTagUniqueIndex";
    private const string TAG_SLUG_SCOPE_MIGRATION = "20260413181605_FixTagSlugAuthorScope";
    private const string PRE_JULY_BASELINE_MIGRATION = "20260422162948_DropTagKindDefault";
    private const string AUTHOR_CONSTRAINT_MIGRATION = "20260712012702_EnforceTagAuthorId";
    private const string ALIAS_UNIQUENESS_MIGRATION = "20260712013310_EnforceSingleCanonicalAlias";
    private const string FALLBACK_PRODUCT_VERSION = "10.0.7";
    private const string EXPECTED_AUTHOR_CONSTRAINT =
        "CHECK ((author_id <> '00000000-0000-0000-0000-000000000000'::uuid))";
    private const string EXPECTED_ALIAS_INDEX =
        "CREATE UNIQUE INDEX ux_tag_aliases_alias_tag_id ON tags.tag_aliases USING btree (alias_tag_id)";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, COMMAND_NAME, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Database connection string is not configured.");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            "SELECT pg_advisory_xact_lock(hashtext('tag-migration-history-reconciliation'));",
            cancellationToken);

        bool tagsTableExists = await ExecuteScalarAsync<bool>(
            connection,
            transaction,
            "SELECT to_regclass('tags.tags') IS NOT NULL;",
            cancellationToken);
        bool aliasesTableExists = await ExecuteScalarAsync<bool>(
            connection,
            transaction,
            "SELECT to_regclass('tags.tag_aliases') IS NOT NULL;",
            cancellationToken);
        bool historyExists = await ExecuteScalarAsync<bool>(
            connection,
            transaction,
            "SELECT to_regclass('tags.\"__EFMigrationsHistory\"') IS NOT NULL;",
            cancellationToken);

        if (!tagsTableExists && !aliasesTableExists && !historyExists)
        {
            Log.Information("Tag schema is empty; migration history reconciliation is not required");
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (tagsTableExists != aliasesTableExists)
        {
            throw new InvalidOperationException(
                "A partial TagService schema was detected. Both tags.tags and " +
                "tags.tag_aliases must exist before migration history can be reconciled.");
        }

        if (!tagsTableExists)
        {
            throw new InvalidOperationException(
                "TagService migration history exists, but its core tables are missing. " +
                "Migration state cannot be reconciled safely.");
        }

        if (!historyExists)
        {
            throw new InvalidOperationException(
                "Tag tables exist, but tags.__EFMigrationsHistory is missing. " +
                "Migration state cannot be reconciled safely.");
        }

        foreach (string requiredMigration in
                 new[]
                 {
                     SQUASHED_INIT_MIGRATION,
                     ENTITY_TAG_INDEX_MIGRATION,
                     TAG_SLUG_SCOPE_MIGRATION,
                     PRE_JULY_BASELINE_MIGRATION,
                 })
        {
            if (!await MigrationAppliedAsync(
                    connection,
                    transaction,
                    requiredMigration,
                    cancellationToken))
            {
                throw new InvalidOperationException(
                    $"TagService core tables exist, but required baseline migration " +
                    $"{requiredMigration} is not recorded.");
            }
        }

        await ReconcileAuthorConstraintAsync(connection, transaction, cancellationToken);
        await ReconcileAliasIndexAsync(connection, transaction, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        Log.Information("TagService migration history reconciliation completed");
    }

    private static async Task ReconcileAuthorConstraintAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        bool migrationApplied = await MigrationAppliedAsync(
            connection,
            transaction,
            AUTHOR_CONSTRAINT_MIGRATION,
            cancellationToken);
        string? constraintDefinition = await ExecuteScalarAsync<string?>(
            connection,
            transaction,
            """
            SELECT pg_get_constraintdef(oid) || CASE WHEN convalidated THEN '' ELSE ' NOT VALID' END
            FROM pg_constraint
            WHERE conrelid = 'tags.tags'::regclass
              AND conname = 'ck_tags_author_id_not_empty';
            """,
            cancellationToken);

        if (migrationApplied)
        {
            EnsureExpectedDefinition(
                "tags.ck_tags_author_id_not_empty",
                constraintDefinition,
                EXPECTED_AUTHOR_CONSTRAINT);
            return;
        }

        if (constraintDefinition is null)
            return;

        EnsureExpectedDefinition(
            "tags.ck_tags_author_id_not_empty",
            constraintDefinition,
            EXPECTED_AUTHOR_CONSTRAINT);
        await RecordMigrationAsync(
            connection,
            transaction,
            AUTHOR_CONSTRAINT_MIGRATION,
            cancellationToken);
        Log.Information(
            "Recorded {MigrationId} because its constraint already exists",
            AUTHOR_CONSTRAINT_MIGRATION);
    }

    private static async Task ReconcileAliasIndexAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        bool migrationApplied = await MigrationAppliedAsync(
            connection,
            transaction,
            ALIAS_UNIQUENESS_MIGRATION,
            cancellationToken);
        string? finalIndexDefinition = await ExecuteScalarAsync<string?>(
            connection,
            transaction,
            """
            SELECT pg_get_indexdef(indexrelid) ||
                   CASE WHEN indisvalid AND indisready THEN '' ELSE ' INVALID' END
            FROM pg_index
            WHERE indexrelid = to_regclass('tags.ux_tag_aliases_alias_tag_id');
            """,
            cancellationToken);
        bool legacyIndexExists = await ExecuteScalarAsync<bool>(
            connection,
            transaction,
            "SELECT to_regclass('tags.\"IX_tag_aliases_alias_tag_id\"') IS NOT NULL;",
            cancellationToken);

        if (migrationApplied)
        {
            EnsureExpectedDefinition(
                "tags.ux_tag_aliases_alias_tag_id",
                finalIndexDefinition,
                EXPECTED_ALIAS_INDEX);
            if (legacyIndexExists)
            {
                await DropLegacyAliasIndexAsync(connection, transaction, cancellationToken);
                Log.Information("Removed redundant legacy TagService alias index");
            }
            return;
        }

        if (finalIndexDefinition is not null)
        {
            EnsureExpectedDefinition(
                "tags.ux_tag_aliases_alias_tag_id",
                finalIndexDefinition,
                EXPECTED_ALIAS_INDEX);
            if (legacyIndexExists)
                await DropLegacyAliasIndexAsync(connection, transaction, cancellationToken);

            await RecordMigrationAsync(
                connection,
                transaction,
                ALIAS_UNIQUENESS_MIGRATION,
                cancellationToken);
            Log.Information(
                "Recorded {MigrationId} because its unique index already exists",
                ALIAS_UNIQUENESS_MIGRATION);
            return;
        }

        if (!legacyIndexExists)
        {
            await ExecuteAsync(
                connection,
                transaction,
                "CREATE INDEX \"IX_tag_aliases_alias_tag_id\" ON tags.tag_aliases (alias_tag_id);",
                cancellationToken);
            Log.Information("Restored legacy TagService alias index for the pending EF migration");
        }
    }

    private static Task DropLegacyAliasIndexAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            "DROP INDEX tags.\"IX_tag_aliases_alias_tag_id\";",
            cancellationToken);

    private static async Task<bool> MigrationAppliedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM tags."__EFMigrationsHistory"
                WHERE "MigrationId" = @migrationId);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("migrationId", migrationId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task RecordMigrationAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string migrationId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO tags."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
            VALUES (
                @migrationId,
                COALESCE(
                    (SELECT "ProductVersion"
                     FROM tags."__EFMigrationsHistory"
                     ORDER BY "MigrationId" DESC
                     LIMIT 1),
                    @fallbackProductVersion))
            ON CONFLICT ("MigrationId") DO NOTHING;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("migrationId", migrationId);
        command.Parameters.AddWithValue("fallbackProductVersion", FALLBACK_PRODUCT_VERSION);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureExpectedDefinition(
        string databaseObject,
        string? actualDefinition,
        string expectedDefinition)
    {
        if (!string.Equals(actualDefinition, expectedDefinition, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Database object {databaseObject} has an unexpected definition. " +
                $"Expected: {expectedDefinition}. Actual: {actualDefinition ?? "missing"}.");
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<T> ExecuteScalarAsync<T>(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null || result is DBNull)
            return default!;
        return (T)result;
    }
}
