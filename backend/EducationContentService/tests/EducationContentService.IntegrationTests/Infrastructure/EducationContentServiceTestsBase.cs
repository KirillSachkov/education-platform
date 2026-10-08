using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ContentAccess.TestSupport;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Tracking;

namespace EducationContentService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class EducationContentServiceTestsBase : IAsyncLifetime
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Func<Task> _resetDatabase;

    protected EducationContentServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        EntitlementChecker = factory.EntitlementChecker;
        Host = factory.Services.GetRequiredService<IHost>();
        AppHttpClient = factory.CreateClient();
        HttpClient = new HttpClient();
        Services = factory.Services;
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IHost Host { get; init; }

    protected IServiceProvider Services { get; init; }

    protected FakeEntitlementChecker EntitlementChecker { get; init; }

    protected HttpClient AppHttpClient { get; init; }
    protected HttpClient HttpClient { get; init; }

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

    protected Task<HttpResponseMessage> PatchAsJsonAsync<TRequest>(string url, TRequest request) =>
        AppHttpClient.PatchAsJsonAsync(url, request);

    /// <summary>
    /// Sends a message and waits until the Wolverine handler finishes processing it.
    /// Uses Wolverine Tracked Sessions — no polling required.
    /// </summary>
    protected async Task InvokeMessageAndWaitAsync<T>(T message) where T : class
    {
        await Host.InvokeMessageAndWaitAsync(message);
    }

    /// <summary>
    /// Sends a message and waits, suppressing handler exceptions.
    /// Useful for testing no-op / "not found" scenarios.
    /// </summary>
    protected async Task InvokeMessageSuppressingExceptionsAsync<T>(T message) where T : class
    {
        await Host
            .TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(message);
    }

    protected async Task ExecuteInDb(Func<EducationDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        EducationDbContext dbContext = scope.ServiceProvider.GetRequiredService<EducationDbContext>();

        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<EducationDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        EducationDbContext dbContext = scope.ServiceProvider.GetRequiredService<EducationDbContext>();

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
