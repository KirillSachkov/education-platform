using Framework.Endpoints;
using PlatformBootstrap;
using ServiceName;
using ServiceName.Persistence;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddPlatformDefaults("ServiceName");

builder.Services
    .AddCore(builder.Configuration)
    .AddInfrastructurePostgres(builder.Configuration);

WebApplication app = builder.Build();

app.UsePlatformDefaults();
app.MapEndpoints();

await app.RunAsync();

namespace ServiceName
{
    public sealed class Program;
}
