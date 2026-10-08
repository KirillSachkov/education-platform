using Microsoft.Extensions.DependencyInjection;
using ServiceName.Infrastructure.Postgres;

namespace ServiceName.IntegrationTests.Infrastructure;

public abstract class ServiceNameTestsBase : IClassFixture<IntegrationTestsWebFactory>, IAsyncLifetime
{
    protected readonly IntegrationTestsWebFactory Factory;
    protected readonly HttpClient Client;

    protected ServiceNameTestsBase(IntegrationTestsWebFactory factory)
    {
        Factory = factory;
        Client = factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Factory.ResetDatabaseAsync();

    protected async Task<T> ExecuteInDbAsync<T>(Func<ServiceNameDbContext, Task<T>> work)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceNameDbContext>();
        return await work(db);
    }
}
