using EducationContentService.Core.Messaging;
using EducationContentService.Web.Configuration;
using Framework.Endpoints;
using PlatformBootstrap;
using Serilog;

try
{
    Log.Information("Starting education content service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    if (ResyncAccessTagsCli.IsRequested(args))
    {
        await ResyncAccessTagsCli.RunAsync(builder.Configuration, builder.Environment);
        return;
    }

    if (DataMigrationsCli.IsRequested(args))
    {
        await DataMigrationsCli.RunAsync(builder.Configuration);
        return;
    }

    if (SeedLevelTestCli.IsRequested(args))
    {
        await SeedLevelTestCli.RunAsync(
            builder.Configuration, builder.Environment, SeedLevelTestCli.HasForceFlag(args));
        return;
    }

    builder.AddPlatformDefaults("EducationContentService");
    builder.Services.AddEducationContentServiceRegistrations(builder.Configuration);
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

namespace EducationContentService.Web
{
    public class Program;
}
