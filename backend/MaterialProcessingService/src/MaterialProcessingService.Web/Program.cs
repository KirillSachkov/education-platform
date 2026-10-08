using System.Globalization;
using Observability;
using Serilog;
using MaterialProcessingService.Core.Messaging;
using MaterialProcessingService.Web.Configuration;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    Log.Information("Starting web application");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    string environment = builder.Environment.EnvironmentName;

    builder.Configuration
        .AddSerilogConfiguration(environment)
        .AddJsonFile($"appsettings.{environment}.json", true, true)
        .AddEnvironmentVariables();

    builder.Services.AddConfiguration(builder.Configuration);

    builder.AddWolverine();

    WebApplication app = builder.Build();

    if (BackfillChaptersFromKinescopeCli.IsRequested(args))
    {
        await BackfillChaptersFromKinescopeCli.RunAsync(app.Services);
        return;
    }

    app.Configure();

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

namespace MaterialProcessingService.Web
{
    public class Program;
}
