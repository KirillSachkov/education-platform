using Framework.Endpoints;
using PlatformBootstrap;
using SearchService.Core.Messaging;
using SearchService.Web;
using SearchService.Web.Configuration;
using Serilog;

try
{
    Log.Information("Starting search service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.AddPlatformDefaults("SearchService");
    builder.Services.AddSearchServiceRegistrations(builder.Configuration);
    builder.AddWolverine();

    WebApplication app = builder.Build();

    await WolverineRestrictionsCleanup.ClearStalePauseRestrictionsAsync(app.Configuration);

    app.UsePlatformDefaults();
    app.MapEndpoints();

    await app.RunAsync();
}
catch (HostAbortedException)
{
    // Expected when running EF migrations tooling
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

namespace SearchService.Web
{
    public class Program;
}
