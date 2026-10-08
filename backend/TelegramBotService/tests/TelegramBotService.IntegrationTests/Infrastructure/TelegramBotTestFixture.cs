using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using TelegramBotService.Infrastructure.Postgres;

namespace TelegramBotService.IntegrationTests.Infrastructure;

/// <summary>
/// Раз-на-collection фикстура: поднимает Postgres через Testcontainers, применяет миграции,
/// инициализирует Respawn для reset между тестами. Используется классами-тестами через
/// <see cref="TelegramBotTestsBase"/>.
///
/// Внутренний DbContext / ServiceCollection — создаются уже в test base, потому что handler'ы
/// scoped и мы хотим свежий scope per-test.
/// </summary>
public sealed class TelegramBotTestFixture : IAsyncLifetime, IDisposable
{
    public void Dispose()
    {
        _dbConnection?.Dispose();
    }


    private readonly PostgreSqlContainer _dbContainer = new PostgreSqlBuilder("postgres:16-alpine")
        .WithDatabase("telegram_bot_service_db_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private Respawner _respawner = null!;
    private DbConnection? _dbConnection;

    public string ConnectionString =>
        _dbContainer.GetConnectionString() + ";Search Path=telegrambot,public";

    public async Task InitializeAsync()
    {
        await _dbContainer.StartAsync();

        DbContextOptions<TelegramBotDbContext> options = new DbContextOptionsBuilder<TelegramBotDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using TelegramBotDbContext dbContext = new(options);

        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();

        _dbConnection = new NpgsqlConnection(ConnectionString);
        await _dbConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_dbConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["telegrambot"],
        });
    }

    public async Task ResetDatabaseAsync()
    {
        if (_dbConnection is null)
            return;
        await _respawner.ResetAsync(_dbConnection);
    }

    public async Task DisposeAsync()
    {
        if (_dbConnection is not null)
        {
            await _dbConnection.CloseAsync();
            await _dbConnection.DisposeAsync();
        }

        await _dbContainer.StopAsync();
        await _dbContainer.DisposeAsync();
    }
}

[CollectionDefinition(nameof(TelegramBotTestCollection))]
public sealed class TelegramBotTestCollection : ICollectionFixture<TelegramBotTestFixture>
{
}
