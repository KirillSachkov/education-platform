using AuthService.Core.Options;
using AuthService.Core.Services;
using Microsoft.Extensions.Options;

namespace AuthService.Web.Configuration;

/// <summary>
/// Seeds and periodically syncs OpenIddict applications and platform roles from appsettings.
/// Uses IOptionsMonitor to react to config changes at runtime (no restart needed).
/// Delegates all sync logic to <see cref="PlatformConfigSyncService"/>.
/// </summary>
public sealed class OpenIddictSeeder : BackgroundService
{
    private static readonly TimeSpan SYNC_INTERVAL = TimeSpan.FromSeconds(60);

    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<OpenIddictOptions> _optionsMonitor;
    private readonly IOptionsMonitor<AuthServiceOptions> _authOptionsMonitor;
    private readonly ILogger<OpenIddictSeeder> _logger;

    public OpenIddictSeeder(
        IServiceProvider serviceProvider,
        IOptionsMonitor<OpenIddictOptions> optionsMonitor,
        IOptionsMonitor<AuthServiceOptions> authOptionsMonitor,
        ILogger<OpenIddictSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _optionsMonitor = optionsMonitor;
        _authOptionsMonitor = authOptionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial sync on startup
        await SyncAsync(stoppingToken);

        // Periodic sync
        using var timer = new PeriodicTimer(SYNC_INTERVAL);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SyncAsync(stoppingToken);
        }
    }

    private async Task SyncAsync(CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _serviceProvider.CreateAsyncScope();
            var syncService = scope.ServiceProvider.GetRequiredService<PlatformConfigSyncService>();
            await syncService.SyncAsync(_optionsMonitor.CurrentValue, _authOptionsMonitor.CurrentValue, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to sync OpenIddict configuration");
        }
    }
}
