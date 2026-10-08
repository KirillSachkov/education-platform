namespace PlatformBootstrap;

/// <summary>
/// Контракт для one-shot CLI команд сервиса (миграции, backfill, recovery).
/// Команда регистрируется в <c>Program.cs</c> через
/// <see cref="PlatformCli.TryRunAsync"/> и запускается раньше, чем поднимается web host.
///
/// Имя команды — первый позиционный аргумент CLI (например, <c>migrate-enrollments-to-plans</c>).
/// Дополнительные флаги (например, <c>--dry-run</c>) команда читает из <c>args</c> сама.
/// </summary>
public interface IPlatformCli
{
    /// <summary>
    /// Имя команды (kebab-case). Сравнивается без учёта регистра с первым нефлаговым
    /// аргументом из <c>args</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Выполняет команду. Получает уже готовый <see cref="IConfiguration"/> от builder'а
    /// (включая <c>appsettings.{Env}.json</c> и env-vars).
    /// </summary>
    Task RunAsync(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        string[] args,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Helpers для запуска <see cref="IPlatformCli"/> из <c>Program.cs</c>. Заменяет boilerplate
/// серии <c>if (XxxCli.IsRequested(args)) { await XxxCli.RunAsync(...); return; }</c>
/// одним вызовом.
///
/// Pattern в <c>Program.cs</c>:
/// <code>
/// WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
///
/// if (await PlatformCli.TryRunAsync(builder, args, [
///     new MigrateEnrollmentsToPlansCli(),
///     new BackfillRedisFromGrantsCli(),
/// ]))
/// {
///     return;
/// }
/// </code>
/// </summary>
public static class PlatformCli
{
    /// <summary>
    /// Если первый нефлаговый <c>args</c> совпадает с одной из <paramref name="commands"/>,
    /// запускает её и возвращает <c>true</c>. Иначе — возвращает <c>false</c> без побочных
    /// эффектов, и caller продолжает обычный bootstrap web host'а.
    /// </summary>
    public static async Task<bool> TryRunAsync(
        WebApplicationBuilder builder,
        string[] args,
        IReadOnlyList<IPlatformCli> commands,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(commands);

        string? requested = args.FirstOrDefault(a => !a.StartsWith('-'));
        if (requested is null)
        {
            return false;
        }

        IPlatformCli? match = commands.FirstOrDefault(
            c => string.Equals(c.Name, requested, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        await match.RunAsync(builder.Configuration, builder.Environment, args, cancellationToken);
        return true;
    }
}
