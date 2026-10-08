using AuthService.Core.Messaging;
using AuthService.Web.Configuration;
using Framework.Endpoints;
using PlatformBootstrap;
using Serilog;

try
{
    Log.Information("Starting auth service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.AddPlatformDefaults("AuthService");

    builder.Services.AddAuthServiceRegistrations(builder.Configuration, builder.Environment);
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

namespace AuthService.Web
{
    public class Program;
}
