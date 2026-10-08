using Framework.Endpoints;
using PlatformBootstrap;
using ProgressService.Core.Messaging;
using ProgressService.Web.Configuration;
using Serilog;

try
{
    Log.Information("Starting progress service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    if (ClearLegacyCourseTagsCli.IsRequested(args))
    {
        await ClearLegacyCourseTagsCli.RunAsync(builder.Configuration, builder.Environment, args);
        return;
    }

    if (await PlatformCli.TryRunAsync(builder, args, [
        new SweepLegacyCourseTagsCli(),
    ]))
    {
        return;
    }

    builder.AddPlatformDefaults("ProgressService");
    builder.Services.AddProgressServiceRegistrations(builder.Configuration);
    builder.AddWolverine();

    WebApplication app = builder.Build();

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

namespace ProgressService.Web
{
    public class Program;
}
