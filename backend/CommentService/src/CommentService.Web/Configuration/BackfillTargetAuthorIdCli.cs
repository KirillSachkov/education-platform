using EducationContentService.Contracts.Ownership;
using Npgsql;
using Serilog;

namespace CommentService.Web.Configuration;

/// <summary>
///     Backfill денормализованной колонки <c>comments.comments.target_author_id</c>
///     для исторических комментов. Cross-schema JOIN с supported targets
///     в EducationContentService (одна БД, разные schemas).
///
///     Запускается автоматически в <c>comment-service-migrations</c> контейнере после
///     <c>efbundle</c> на каждом деплое. Идемпотентно сверяет и NULL, и устаревшие значения
///     с тем же binding-priority, который использует ECS ownership endpoint.
///     Не требует поднятия RabbitMQ / Redis / прочих зависимостей CommentService.
///
///     Manual run: <c>docker exec comment-service dotnet CommentService.Web.dll backfill-target-author-id</c>
/// </summary>
public static class BackfillTargetAuthorIdCli
{
    public const string COMMAND_NAME = "backfill-target-author-id";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, COMMAND_NAME, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        Log.Information("Starting target_author_id backfill");

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Migration containers may start concurrently. The published view is the sole
        // compatibility boundary: if ECS has not installed it yet, the next deploy retries
        // this idempotent reconciliation without coupling CommentService to private tables.
        await using (var viewCheck = new NpgsqlCommand(
                         "SELECT to_regclass(@ViewName) IS NOT NULL;",
                         connection))
        {
            viewCheck.Parameters.AddWithValue(
                "ViewName",
                EntityOwnershipDatabaseContract.COMMENT_TARGET_OWNERSHIP_VIEW_V1);
            object? viewExists = await viewCheck.ExecuteScalarAsync(cancellationToken);
            if (viewExists is not true)
            {
                Log.Information(
                    "ECS ownership view v1 is not present — skipping target_author_id backfill");
                return;
            }
        }

        string sql = $"""
                           UPDATE comments.comments c
                           SET target_author_id = owner.author_id
                           FROM {EntityOwnershipDatabaseContract.COMMENT_TARGET_OWNERSHIP_VIEW_V1} owner
                           WHERE c.target_entity_type = owner.target_entity_type
                             AND c.target_entity_id = owner.target_entity_id
                             AND c.target_author_id IS DISTINCT FROM owner.author_id;
                           """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.CommandTimeout = 300;
        int updated = await cmd.ExecuteNonQueryAsync(cancellationToken);

        Log.Information("Reconciled target_author_id for {Count} comments", updated);
    }
}
