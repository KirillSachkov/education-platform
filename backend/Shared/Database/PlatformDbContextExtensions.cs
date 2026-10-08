namespace PlatformDatabase;

/// <summary>
/// Cross-cutting EF Core + Npgsql defaults shared by every service's
/// <c>Infrastructure.Postgres</c> registration.
/// </summary>
public static class PlatformDbContextExtensions
{
    /// <summary>
    /// Drop-in replacement for <c>UseNpgsql</c> that applies platform-wide
    /// connection-string defaults before delegating to the standard provider.
    /// </summary>
    /// <remarks>
    /// Currently sets <c>GssEncryptionMode=Disable</c> — Npgsql 10 changed the
    /// default to <c>Prefer</c>, which probes <c>libgssapi_krb5.so.2</c> at
    /// startup and emits "Cannot load library …" on every slim Debian-based
    /// image because the lib isn't pre-installed. Disabling here removes the
    /// noise globally without shipping libgssapi-krb5-2 in every Dockerfile.
    /// Future cross-cutting Npgsql defaults (ApplicationName, CommandTimeout,
    /// SSL, etc.) belong here too.
    /// </remarks>
    public static DbContextOptionsBuilder UsePlatformNpgsql(
        this DbContextOptionsBuilder options,
        string? connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        string normalized = NormalizeConnectionString(connectionString);

        return npgsqlOptionsAction is null
            ? options.UseNpgsql(normalized)
            : options.UseNpgsql(normalized, npgsqlOptionsAction);
    }

    /// <summary>
    /// Public helper for non-EF call sites (Wolverine outbox, Dapper data sources, raw
    /// <see cref="NpgsqlDataSource"/>) that need the same platform defaults as
    /// <see cref="UsePlatformNpgsql"/> but bypass the EF Core extension chain.
    /// </summary>
    public static string WithPlatformDefaults(this string connectionString)
    {
        return NormalizeConnectionString(connectionString);
    }

    private static string NormalizeConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Postgres connection string is null or empty. " +
                "Check ConnectionStrings:Database in configuration / env.");
        }

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            GssEncryptionMode = GssEncryptionMode.Disable,
        };

        return builder.ConnectionString;
    }
}
