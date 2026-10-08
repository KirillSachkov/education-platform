using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProgressService.Domain.Enrollments;
using ProgressService.Infrastructure.Postgres;
using Wolverine.Tracking;

namespace ProgressService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class ProgressServiceTestsBase : IAsyncLifetime
{
    private readonly Func<Task> _resetDatabase;

    protected ProgressServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        EducationContentClient = factory.EducationContentClient;
        AuthServiceClient = factory.AuthServiceClient;
        AccessServiceClient = factory.AccessServiceClient;
        UserGrantWriter = factory.UserGrantWriter;
        EntitlementChecker = factory.EntitlementChecker;
        Host = factory.Services.GetRequiredService<IHost>();
        AppHttpClient = factory.CreateClient();
        HttpClient = new HttpClient();
        Services = factory.Services;
        DomainEventSpy = factory.Services.GetRequiredService<DomainEventSpy>();
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IHost Host { get; init; }

    protected IServiceProvider Services { get; init; }

    protected MockEducationContentServiceClient EducationContentClient { get; init; }
    protected MockAuthServiceClient AuthServiceClient { get; init; }
    protected MockAccessServiceClient AccessServiceClient { get; init; }
    protected FakeUserGrantWriter UserGrantWriter { get; init; }
    protected ContentAccess.TestSupport.FakeEntitlementChecker EntitlementChecker { get; init; }

    protected HttpClient AppHttpClient { get; init; }
    protected HttpClient HttpClient { get; init; }
    protected DomainEventSpy DomainEventSpy { get; init; }

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

    protected Task<HttpResponseMessage> PostAsJsonAsync<TRequest>(string url, TRequest request) =>
        AppHttpClient.PostAsJsonAsync(url, request);

    protected Task<HttpResponseMessage> PostAsync(string url) =>
        AppHttpClient.PostAsync(url, content: null);

    protected static async Task<T> ReadWrappedResultAsync<T>(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();

        JsonSerializerOptions options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out JsonElement wrappedResult))
        {
            return wrappedResult.Deserialize<T>(options)!;
        }

        return root.Deserialize<T>(options)!;
    }

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

    /// <summary>
    /// Seeds a <see cref="CourseEnrollment"/> progress anchor directly in the DB.
    /// access-derive-model Phase 4: the per-course enroll-management endpoints
    /// (<c>/enroll</c>, <c>enroll-by-ids</c>, ...) are gone; enrollments are lazy
    /// progress anchors. Tests that need an anchor pre-existing seed it here instead
    /// of hitting a deleted endpoint. Idempotent on (userId, courseId).
    /// </summary>
    protected async Task SeedEnrollmentAsync(Guid courseId, Guid userId, Guid? authorId = null)
    {
        await ExecuteInDb(async db =>
        {
            bool exists = db.CourseEnrollments.Any(e => e.UserId == userId && e.CourseId == courseId);
            if (exists)
            {
                return;
            }

            CourseEnrollment anchor = CourseEnrollment.CreateAnchor(
                userId,
                courseId,
                authorId ?? Guid.NewGuid(),
                EnrollmentSource.ENGAGEMENT).Value;

            await db.CourseEnrollments.AddAsync(anchor);
            await db.SaveChangesAsync();
        });
    }

    protected async Task ExecuteInDb(Func<ProgressDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        ProgressDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProgressDbContext>();

        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<ProgressDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        ProgressDbContext dbContext = scope.ServiceProvider.GetRequiredService<ProgressDbContext>();

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
        DomainEventSpy.Clear();
        NoOpOutboxService.Reset();
        AccessServiceClient.Reset();
        await _resetDatabase();
    }
}
