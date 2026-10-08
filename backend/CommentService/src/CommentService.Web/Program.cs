using CommentService.Core.Messaging;
using CommentService.Web;
using CommentService.Web.Configuration;
using Framework.Endpoints;
using PlatformBootstrap;
using Serilog;

try
{
    Log.Information("Starting comments service");

    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    if (BackfillTargetAuthorIdCli.IsRequested(args))
    {
        await BackfillTargetAuthorIdCli.RunAsync(builder.Configuration);
        return;
    }

    builder.AddPlatformDefaults("CommentService");
    builder.Services.AddCommentServiceRegistrations(builder.Configuration);

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

namespace CommentService.Web
{
    public partial class Program;
}
