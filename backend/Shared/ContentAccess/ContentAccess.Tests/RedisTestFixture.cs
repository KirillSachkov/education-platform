using StackExchange.Redis;
using Testcontainers.Redis;

namespace ContentAccess.Tests;

[CollectionDefinition(nameof(RedisTestCollection))]
public sealed class RedisTestCollection : ICollectionFixture<RedisTestFixture>;

public sealed class RedisTestFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7.2").Build();

    public IConnectionMultiplexer Redis { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Redis = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Redis.DisposeAsync();
        await _container.DisposeAsync();
    }
}
