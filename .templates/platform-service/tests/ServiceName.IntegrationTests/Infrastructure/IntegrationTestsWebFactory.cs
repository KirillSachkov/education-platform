using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Respawn;
using ServiceName.Infrastructure.Postgres;
using ServiceName.Web;
using Testcontainers.PostgreSql;

namespace ServiceName.IntegrationTests.Infrastructure;

public sealed class IntegrationTestsWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pgContainer = new PostgreSqlBuilder("postgres")
        .WithDatabase("__SERVICE_SCHEMA___testdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection _dbConnection = null!;

    private string ConnectionString => _pgContainer.GetConnectionString() + ";Search Path=__SERVICE_SCHEMA__,public";

    public async Task InitializeAsync()
    {
        await _pgContainer.StartAsync();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceNameDbContext>();
        await db.Database.MigrateAsync();

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(
            _dbConnection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["__SERVICE_SCHEMA__"],
            });
    }

    public Task ResetDatabaseAsync() => _respawner.ResetAsync(_dbConnection);

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();

        if (_dbConnection is not null)
        {
            await _dbConnection.CloseAsync();
            await _dbConnection.DisposeAsync();
        }

        await _pgContainer.StopAsync();
        await _pgContainer.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Database", ConnectionString);

        builder.ConfigureServices(services =>
        {
            // Drop the RabbitMQ health check if the service registers one — integration
            // tests don't run a broker.
            services.PostConfigure<HealthCheckServiceOptions>(opts =>
            {
                var toRemove = opts.Registrations.Where(x => x.Name == "rabbitmq").ToList();
                foreach (var r in toRemove)
                    opts.Registrations.Remove(r);
            });
        });
    }
}
