using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MaterialProcessingService.Infrastructure.Postgres;
using Wolverine.Tracking;

namespace MaterialProcessingService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class MaterialProcessingServiceTestsBase : IAsyncLifetime
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Func<Task> _resetDatabase;

    protected MaterialProcessingServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        Factory = factory;
        Host = factory.Services.GetRequiredService<IHost>();
        Services = factory.Services;
        AppHttpClient = factory.CreateClient();
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IntegrationTestsWebFactory Factory { get; }

    protected IHost Host { get; }

    protected IServiceProvider Services { get; }

    protected HttpClient AppHttpClient { get; }

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

    protected async Task InvokeMessageAndWaitAsync<T>(T message) where T : class
    {
        await Host.InvokeMessageAndWaitAsync(message);
    }

    protected async Task ExecuteInDb(Func<MaterialProcessingServiceDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MaterialProcessingServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<MaterialProcessingServiceDbContext>();
        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<MaterialProcessingServiceDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MaterialProcessingServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<MaterialProcessingServiceDbContext>();
        return await action(dbContext);
    }

    protected static async Task<T> ReadResultAsync<T>(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(payload);
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("result", out JsonElement resultElement))
            return resultElement.Deserialize<T>(_jsonOptions)!;

        return root.Deserialize<T>(_jsonOptions)!;
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
