using System.Net.Http.Headers;
using AccessService.Contracts.HttpCommunication;
using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotificationService.Infrastructure.Postgres;
using Shared.Email;
using Wolverine.Testing;
using Wolverine.Tracking;

namespace NotificationService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class NotificationServiceTestsBase : IAsyncLifetime
{
    private readonly Func<Task> _resetDatabase;

    protected NotificationServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        AppHttpClient = factory.CreateClient();
        Services = factory.Services;
        Host = factory.Services.GetRequiredService<IHost>();
        AuthServiceClient = factory.AuthServiceClient;
        EducationContentClient = factory.EducationContentClient;
        AccessServiceClient = factory.AccessServiceClient;
        EmailSender = factory.EmailSender;
        OutboxCollector = factory.OutboxCollector;
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IServiceProvider Services { get; init; }
    protected IHost Host { get; init; }
    protected HttpClient AppHttpClient { get; init; }
    protected IAuthServiceClient AuthServiceClient { get; init; }
    protected IEducationContentServiceClient EducationContentClient { get; init; }
    protected IAccessServiceClient AccessServiceClient { get; init; }
    protected IEmailSender EmailSender { get; init; }
    protected TestOutboxCollector OutboxCollector { get; init; }

    protected void AuthenticateAs(Guid userId, params string[] groups)
    {
        AppHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateToken(userId, groups));
    }

    protected void AuthenticateAsAdmin(Guid? userId = null)
    {
        AppHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateAdminToken(userId));
    }

    protected void RemoveAuthentication()
    {
        AppHttpClient.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>
    /// Sends an integration event through Wolverine and waits for all handlers (and their
    /// cascaded internal messages — e.g. NotificationCreated → SseFanoutHandler) to finish.
    /// Default Wolverine timeout (5s) выкручен до 30s — full test suite иногда тормозит на
    /// тестах с каскадом (broadcast → 3 NotificationCreated → 3 SseFanout) под нагрузкой
    /// параллельных контейнеров.
    /// </summary>
    protected async Task InvokeMessageAndWaitAsync<T>(T message) where T : class
    {
        await Host.InvokeMessageAndWaitAsync(message, timeoutInMilliseconds: 30_000);
    }

    protected async Task ExecuteInDb(Func<NotificationDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NotificationDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<NotificationDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        NotificationDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        return await action(dbContext);
    }

    public Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        RemoveAuthentication();
        await _resetDatabase();
    }
}
