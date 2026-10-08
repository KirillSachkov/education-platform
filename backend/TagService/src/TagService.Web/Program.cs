using Framework.Endpoints;
using PlatformBootstrap;
using Serilog;
using TagService.Core.Messaging;
using TagService.Web;
using TagService.Web.Configuration;

try
{
    Log.Information("Starting tag service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    if (TagMigrationHistoryReconciliationCli.IsRequested(args))
    {
        await TagMigrationHistoryReconciliationCli.RunAsync(builder.Configuration);
        return;
    }

    builder.AddPlatformDefaults("TagService");
    builder.Services.AddTagServiceRegistrations(builder.Configuration);
    builder.AddWolverine();

    WebApplication app = builder.Build();

    app.UsePlatformDefaults();
    app.MapEndpoints();

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

namespace TagService.Web
{
    public partial class Program;
}
