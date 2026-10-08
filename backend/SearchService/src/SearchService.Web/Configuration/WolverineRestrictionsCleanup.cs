using Npgsql;
using SearchService.Core;
using Serilog;

namespace SearchService.Web.Configuration;

/// <summary>
/// Чистит «зависшие» <c>Paused</c> записи в <c>search.wolverine_agent_restrictions</c> при старте процесса.
///
/// Контекст: <see cref="Core.Messaging.WolverineSearchIndexingConsumerController"/> ставит на паузу
/// очереди RabbitMQ во время full reindex, чтобы lifecycle-события не уходили в alias=старая_коллекция,
/// которую дропают после atomic alias swap. Если реиндекс прерван (краш процесса, rolling deploy
/// в середине), <c>ResumeScope.DisposeAsync</c> не отрабатывает, и <c>Paused</c> записи остаются в БД.
/// Wolverine читает их при следующем старте и держит листенеры в паузе бессрочно — search molchit.
///
/// На старте процесса мы заведомо не находимся внутри реиндекса (он только что начнёт работать),
/// поэтому любые сохранённые <c>Paused</c> ограничения — мусор от прерванного прогона. Чистим до
/// того, как стартует Wolverine HostedService.
/// </summary>
internal static class WolverineRestrictionsCleanup
{
    private const string DELETE_STALE_PAUSED_SQL =
        "DELETE FROM search.wolverine_agent_restrictions WHERE type = 'Paused'";

    public static async Task ClearStalePauseRestrictionsAsync(IConfiguration configuration)
    {
        string connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringNames.DATABASE} is required");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(DELETE_STALE_PAUSED_SQL, connection);

        int rowsDeleted;
        try
        {
            rowsDeleted = await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (string.Equals(ex.SqlState, "42P01", StringComparison.Ordinal))
        {
            // First deploy: schema/tables ещё не созданы Wolverine'ом — чистить нечего.
            return;
        }

        if (rowsDeleted > 0)
        {
            Log.Warning(
                "Cleared {RowsDeleted} stale 'Paused' Wolverine agent restrictions on startup. "
                + "Previous reindex was interrupted before the consumer pause could be lifted.",
                rowsDeleted);
        }
    }
}
