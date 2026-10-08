using System.Net.Http.Headers;
using System.Net.Http.Json;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using Wolverine;
using Wolverine.Testing;

namespace AssignmentReviewService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class AssignmentReviewServiceTestsBase : IAsyncLifetime
{
    public static readonly Guid DefaultUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Func<Task> _resetDatabase;

    protected AssignmentReviewServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        Factory = factory;
        AppHttpClient = factory.CreateClient();
        Services = factory.Services;
        _resetDatabase = factory.ResetDatabaseAsync;
        OutboxCollector = factory.OutboxCollector;
    }

    protected IntegrationTestsWebFactory Factory { get; }

    protected IServiceProvider Services { get; }

    protected HttpClient AppHttpClient { get; }

    /// <summary>
    ///     Pattern A outbox collector. Assert integration-event publishes via
    ///     <c>OutboxCollector.OfType&lt;T&gt;()</c>. Cleared automatically before each test.
    /// </summary>
    protected TestOutboxCollector OutboxCollector { get; }

    protected Guid CurrentUserId { get; private set; } = DefaultUserId;

    protected void AuthenticateAs(string role, Guid? userId = null)
    {
        CurrentUserId = userId ?? DefaultUserId;
        AppHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateToken(CurrentUserId, role));
    }

    protected void RemoveAuthentication()
    {
        AppHttpClient.DefaultRequestHeaders.Authorization = null;
    }

    protected async Task ExecuteInDbAsync(Func<AssignmentReviewServiceDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssignmentReviewServiceDbContext db =
            scope.ServiceProvider.GetRequiredService<AssignmentReviewServiceDbContext>();
        await action(db);
    }

    protected async Task<T> ExecuteInDbAsync<T>(Func<AssignmentReviewServiceDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssignmentReviewServiceDbContext db =
            scope.ServiceProvider.GetRequiredService<AssignmentReviewServiceDbContext>();
        return await action(db);
    }

    /// <summary>
    ///     POST /run-iteration/ + in-process invoke Wolverine handler.
    ///
    ///     After #357 the endpoint is fire-and-forget: it publishes a durable
    ///     <see cref="RunAiReviewRequested"/> command and returns 200 with
    ///     <c>{ aiReviewId, status: "QUEUED" }</c>. Wolverine then picks it up in
    ///     a separate scope and runs the LLM pipeline. In tests we don't want to
    ///     wait for the durability agent (it's disabled in Pattern A) — we drive
    ///     the handler directly via <see cref="IMessageBus.InvokeAsync"/> so the
    ///     iteration completes synchronously and assertions on DB state remain
    ///     deterministic. The endpoint's fast-fail paths (404 / 403 / 409 / 400)
    ///     skip the invoke; tests of those paths just check the response code.
    /// </summary>
    protected async Task<HttpResponseMessage> PostRunIterationAsync(
        Guid reviewId, string? modelOverride = null)
    {
        string url = modelOverride is null
            ? $"/assignment-review/reviews/{reviewId}/run-iteration/"
            : $"/assignment-review/reviews/{reviewId}/run-iteration/?modelOverride={Uri.EscapeDataString(modelOverride)}";
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(url, new { });

        if (response.IsSuccessStatusCode)
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(new RunAiReviewRequested(reviewId, modelOverride));
        }

        return response;
    }

    public Task InitializeAsync() => _resetDatabase();

    public Task DisposeAsync()
    {
        RemoveAuthentication();
        return Task.CompletedTask;
    }
}
