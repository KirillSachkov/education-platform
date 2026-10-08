using NotificationService.Core.Messaging;
using NotificationService.Web;
using PlatformBootstrap;
using Serilog;

try
{
    Log.Information("Starting notification service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.AddPlatformDefaults("NotificationService");
    builder.Services.AddNotificationServiceRegistrations(builder.Configuration);

    builder.AddWolverine();

    WebApplication app = builder.Build();

    app.UsePlatformDefaults();
    app.MapNotificationEndpoints();

    await app.RunAsync();
}
catch (HostAbortedException)
{
    // Expected on `dotnet ef migrations` / `dotnet ef bundle --apply` — the host
    // is aborted after the migration runs, which should NOT be logged as Fatal.
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

namespace NotificationService.Web
{
    public partial class Program;
}
