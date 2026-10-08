using FileService.Core.Messaging;
using FileService.Web.Configuration;
using Framework.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlatformBootstrap;
using Serilog;

try
{
    Log.Information("Starting file service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.AddPlatformDefaults("FileService");
    builder.Services.AddFileServiceRegistrations(builder.Configuration);
    builder.AddWolverine();

    WebApplication app = builder.Build();

    // #646 backfill CLI: generate responsive image variants for existing image assets.
    // Runs against the full DI graph (S3 + DbContext + renderer) then exits.
    if (GenerateImageVariantsCli.IsRequested(args))
    {
        await GenerateImageVariantsCli.RunAsync(
            app.Services,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GenerateImageVariantsCli"));
        return;
    }

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

namespace FileService.Web
{
    public class Program;
}
