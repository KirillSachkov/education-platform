using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using ServiceName.Persistence;

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

    public Task DisposeAsync()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        return Factory.ResetDatabaseAsync();
    }

    protected void AuthenticateAs(Guid userId, params string[] roles)
    {
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateToken(userId, roles));
    }

    protected void AuthenticateAsAdmin(Guid? userId = null)
    {
        Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateAdminToken(userId));
    }

    protected async Task<T> ExecuteInDbAsync<T>(Func<ServiceNameDbContext, Task<T>> work)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceNameDbContext>();
        return await work(db);
    }
}
