using Serilog;

namespace EducationContentService.Web.Configuration;

/// <summary>
///     CLI: применить все ожидающие data-migration скрипты.
///     Usage: <c>dotnet EducationContentService.Web.dll migrate-data</c>.
///     Запускается migration-контейнером после <c>efbundle</c>.
///     Безопасно повторять — идемпотентно через трекинг <c>__data_migrations_history</c>.
/// </summary>
public static class DataMigrationsCli
{
    public const string CommandName = "migrate-data";

    public static bool IsRequested(string[] args) =>
        args.Any(x => string.Equals(x, CommandName, StringComparison.OrdinalIgnoreCase));

    public static async Task RunAsync(
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        string? connectionString = configuration.GetConnectionString("Database");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is not configured.");
        }

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddSerilog());

        await using ServiceProvider provider = services.BuildServiceProvider();

        ILogger<DataMigrationRunner> logger = provider.GetRequiredService<ILogger<DataMigrationRunner>>();
        var runner = new DataMigrationRunner(connectionString, logger);

        await runner.ApplyAllAsync(cancellationToken);
    }
}
