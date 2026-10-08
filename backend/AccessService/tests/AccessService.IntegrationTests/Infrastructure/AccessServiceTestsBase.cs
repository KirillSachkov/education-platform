using System.Net.Http.Headers;
using AccessService.Infrastructure.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Wolverine.Testing;

namespace AccessService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class AccessServiceTestsBase : IAsyncLifetime
{
    public static readonly Guid DefaultUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Func<Task> _resetDatabase;

    protected AccessServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        Factory = factory;
        OutboxCollector = factory.OutboxCollector;
        AppHttpClient = factory.CreateClient();
        Services = factory.Services;
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IntegrationTestsWebFactory Factory { get; }

    protected TestOutboxCollector OutboxCollector { get; }

    protected IServiceProvider Services { get; }

    protected HttpClient AppHttpClient { get; }

    protected Guid CurrentUserId { get; private set; } = DefaultUserId;

    protected void AuthenticateAs(string role, Guid? userId = null)
    {
        CurrentUserId = userId ?? DefaultUserId;
        AppHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateToken(CurrentUserId, role));
    }

    protected void AuthenticateAsAdmin(Guid? userId = null)
    {
        CurrentUserId = userId ?? DefaultUserId;
        AppHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateAdminToken(CurrentUserId));
    }

    protected void RemoveAuthentication()
    {
        AppHttpClient.DefaultRequestHeaders.Authorization = null;
    }

    protected async Task ExecuteInDbAsync(Func<AccessServiceDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AccessServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<AccessServiceDbContext>();
        await action(dbContext);
    }

    protected async Task<T> ExecuteInDbAsync<T>(Func<AccessServiceDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AccessServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<AccessServiceDbContext>();
        return await action(dbContext);
    }

    public async Task InitializeAsync()
    {
        await _resetDatabase();
        Factory.TBankClient.Reset();
        Factory.TelegramClient.Reset();
        Factory.EntitlementChecker.Reset();
        Factory.GitHubApi.Reset();
        Factory.AuthClient.Reset();
        Factory.EduClient.ResetMaterialSummaries();
        Factory.EduClient.ResetCourseLookups();
        AuthenticateAs("platform-author");
    }

    public Task DisposeAsync()
    {
        RemoveAuthentication();
        return Task.CompletedTask;
    }
}
