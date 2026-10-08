using System.Globalization;
using System.Reflection;
using Npgsql;

namespace EducationContentService.Web.Configuration;

/// <summary>
///     Применяет data-migration скрипты (<c>.sql</c>) в алфавитном порядке,
///     треккая применённые в таблице <c>education.__data_migrations_history</c>.
///     <para>
///     Схемные миграции остаются под EF Core (<c>efbundle</c>). Data-migrations
///     живут отдельно — backfill, нормализация, bulk-UPDATE — чтобы их можно было
///     писать идемпотентно, ревьюить как обычный SQL и перезапускать без страха.
///     </para>
///     <para>
///     Правила для скриптов:
///     <list type="bullet">
///         <item>Имя — <c>YYYYMMDD_NNN_description.sql</c>. Применяются по алфавиту.</item>
///         <item>Каждый скрипт ОБЯЗАН быть идемпотентным: <c>WHERE col != 'new'</c>,
///               <c>INSERT ... WHERE NOT EXISTS</c>, <c>IF NOT EXISTS</c>.</item>
///         <item>Весь скрипт оборачивается в транзакцию runner'ом — не вставлять <c>BEGIN/COMMIT</c>.</item>
///         <item>Скрипт применяется максимум один раз, имя сохраняется в history. Если хочешь
///               перезапустить — удали строку из <c>__data_migrations_history</c>.</item>
///     </list>
///     </para>
/// </summary>
public sealed class DataMigrationRunner
{
    private const string HistoryTableSchema = "education";
    private const string HistoryTableName = "__data_migrations_history";
    private const string ResourceNamespace = "EducationContentService.Web.DataMigrations";

    private readonly string _connectionString;
    private readonly ILogger<DataMigrationRunner> _logger;

    public DataMigrationRunner(string connectionString, ILogger<DataMigrationRunner> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task ApplyAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureHistoryTableAsync(connection, cancellationToken);

        HashSet<string> applied = await LoadAppliedAsync(connection, cancellationToken);

        IReadOnlyList<(string Name, string Sql)> scripts = LoadScriptsFromResources();

        _logger.LogInformation(
            "Data-migrations: found {Total} script(s), {Applied} already applied, {Pending} pending",
            scripts.Count, applied.Count, scripts.Count(s => !applied.Contains(s.Name)));

        foreach ((string name, string sql) in scripts)
        {
            if (applied.Contains(name))
            {
                _logger.LogDebug("Data-migration {Name} already applied, skipping", name);
                continue;
            }

            await ApplyOneAsync(connection, name, sql, cancellationToken);
        }

        _logger.LogInformation("Data-migrations: all pending scripts applied");
    }

    private static async Task EnsureHistoryTableAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        string sql = $"""
            CREATE TABLE IF NOT EXISTS {HistoryTableSchema}.{HistoryTableName} (
                migration_id  text PRIMARY KEY,
                applied_at    timestamptz NOT NULL DEFAULT timezone('utc', now()),
                duration_ms   integer     NOT NULL
            );
            """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<HashSet<string>> LoadAppliedAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        string sql = $"SELECT migration_id FROM {HistoryTableSchema}.{HistoryTableName};";
        await using var cmd = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private async Task ApplyOneAsync(NpgsqlConnection connection, string name, string sql, CancellationToken ct)
    {
        _logger.LogInformation("Applying data-migration {Name}...", name);
        long startTicks = Environment.TickCount64;

        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync(ct);
        try
        {
            // SQL приходит из embedded resource, не от user — CA2100 неактуально.
#pragma warning disable CA2100
            await using (var scriptCmd = new NpgsqlCommand(sql, connection, tx))
#pragma warning restore CA2100
            {
                await scriptCmd.ExecuteNonQueryAsync(ct);
            }

            int durationMs = (int)(Environment.TickCount64 - startTicks);

            await using (var historyCmd = new NpgsqlCommand(
                $"INSERT INTO {HistoryTableSchema}.{HistoryTableName} (migration_id, duration_ms) VALUES (@id, @dur);",
                connection, tx))
            {
                historyCmd.Parameters.AddWithValue("@id", name);
                historyCmd.Parameters.AddWithValue("@dur", durationMs);
                await historyCmd.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            _logger.LogInformation("Applied {Name} in {Duration}ms", name, durationMs);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static IReadOnlyList<(string Name, string Sql)> LoadScriptsFromResources()
    {
        Assembly assembly = typeof(DataMigrationRunner).Assembly;
        string prefix = ResourceNamespace + ".";

        var scripts = new List<(string Name, string Sql)>();
        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (!resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                continue;

            string name = resourceName.Substring(prefix.Length);
            using Stream? stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing resource stream for {resourceName}");
            using var reader = new StreamReader(stream);
            string sql = reader.ReadToEnd();
            scripts.Add((name, sql));
        }

        // Сортируем по имени — префикс YYYYMMDD_NNN даёт детерминированный порядок.
        scripts.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return scripts;
    }

    // Helper для отладки: распечатать список scripts в логах с длиной
    internal static IReadOnlyList<string> ListScriptNames()
    {
        return LoadScriptsFromResources()
            .Select(s => $"{s.Name} ({s.Sql.Length.ToString(CultureInfo.InvariantCulture)} chars)")
            .ToList();
    }
}
