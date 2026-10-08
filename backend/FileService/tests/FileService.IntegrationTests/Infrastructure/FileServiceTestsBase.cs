using System.Net.Http.Headers;
using Amazon.S3;
using FileService.Infrastructure.Postgres;
using FileService.Core.Services.AssetRegistry;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Testing;
using Wolverine.Tracking;

namespace FileService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class FileServiceTestsBase : IAsyncLifetime
{
    public const string TEST_FILE_NAME = "test-file.mp4";

    private readonly IntegrationTestsWebFactory _factory;

    protected FileServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        _factory = factory;
        Host = factory.Services.GetRequiredService<IHost>();
        AppHttpClient = factory.CreateClient();
        HttpClient = new HttpClient();
        Services = factory.Services;
        OutboxCollector = factory.OutboxCollector;
    }

    public async Task InitializeAsync()
    {
        AuthenticateAsAdmin();
        await _factory.ResetDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        RemoveAuthentication();
        return Task.CompletedTask;
    }

    protected IHost Host { get; init; }

    protected IServiceProvider Services { get; init; }

    protected HttpClient AppHttpClient { get; init; }
    protected HttpClient HttpClient { get; init; }

    protected TestOutboxCollector OutboxCollector { get; }

    protected ITargetEntityAuthorization TargetEntityAuthorization => _factory.TargetEntityAuthorization;

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

    protected HttpClient CreateAppClient(bool allowAutoRedirect)
    {
        HttpClient client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = allowAutoRedirect,
        });

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtHelper.GenerateAdminToken());

        return client;
    }

    protected async Task InvokeMessageAndWaitAsync<T>(T message)
        where T : class
    {
        await Host.InvokeMessageAndWaitAsync(message);
    }

    protected async Task InvokeMessageSuppressingExceptionsAsync<T>(T message)
        where T : class
    {
        await Host
            .TrackActivity()
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(message);
    }

    protected async Task ExecuteInDb(Func<FileServiceDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        FileServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<FileServiceDbContext>();

        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<FileServiceDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        FileServiceDbContext dbContext = scope.ServiceProvider.GetRequiredService<FileServiceDbContext>();
        return await action(dbContext);
    }

    protected async Task ExecuteInS3(Func<IAmazonS3, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        IAmazonS3 s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        await action(s3Client);
    }
}
