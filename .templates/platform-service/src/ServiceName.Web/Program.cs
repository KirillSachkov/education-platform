using Framework.Endpoints;
using PlatformBootstrap;
using ServiceName.Core;
using ServiceName.Infrastructure.Postgres;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddPlatformDefaults("ServiceName");

builder.Services
    .AddCore(builder.Configuration)
    .AddInfrastructurePostgres(builder.Configuration);

WebApplication app = builder.Build();

app.UsePlatformDefaults();
app.MapEndpoints();

await app.RunAsync();

namespace ServiceName.Web
{
    public sealed class Program;
}
