using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlatformAuth.Middleware;
using Common;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using SearchService.Contracts;
using SearchService.Core;
using SearchService.Core.Features.EducationDocuments.Queries;
using SearchService.Domain;
using StackExchange.Redis;
using ContentAccess;
using ContentAccess.Redis;
using SearchService.Core.Features.Reindex.IntegrationEvents;
using SearchService.IntegrationTests.Mocks;
using Wolverine.Testing;
using Wolverine.Tracking;

namespace SearchService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class SearchServiceTestsBase : IAsyncLifetime
{
    private readonly Func<Task> _resetDatabase;

    protected SearchServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        Host = factory.Services.GetRequiredService<IHost>();
        AppHttpClient = factory.CreateClient();
        Services = factory.Services;
        _resetDatabase = factory.ResetDatabaseAsync;
        OutboxCollector = factory.OutboxCollector;
    }

    protected IHost Host { get; init; }

    protected IServiceProvider Services { get; init; }

    protected HttpClient AppHttpClient { get; init; }

    /// <summary>
    ///     Pattern A outbox collector. Assert integration-event publishes via
    ///     <c>OutboxCollector.OfType&lt;T&gt;()</c>. Cleared automatically before each test.
    /// </summary>
    protected TestOutboxCollector OutboxCollector { get; init; }
    
    /// <summary>
    /// Sends a message and waits until the Wolverine handler finishes processing it.
    /// Uses Wolverine Tracked Sessions - no polling required.
    /// </summary>
    protected async Task InvokeMessageAndWaitAsync<T>(T message) where T : class
    {
        await Host
            .TrackActivity(TimeSpan.FromSeconds(30))
            .InvokeMessageAndWaitAsync(message);
    }

    /// <summary>
    /// Sends a message and waits, suppressing handler exceptions.
    /// Useful for testing no-op / "not found" scenarios.
    /// </summary>
    protected async Task InvokeMessageSuppressingExceptionsAsync<T>(T message) where T : class
    {
        await Host
            .TrackActivity(TimeSpan.FromSeconds(30))
            .DoNotAssertOnExceptionsDetected()
            .InvokeMessageAndWaitAsync(message);
    }

    protected async Task<EducationDocument?> FindDocumentAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchProvider searchProvider = scope.ServiceProvider.GetRequiredService<ISearchProvider>();

        var result = await searchProvider.ExportByIdAsync<EducationDocument>(
            CollectionNames.EDUCATION_SEARCH,
            id,
            cancellationToken);

        if (result.IsFailure)
            throw result.Error.ToException();

        return result.Value;
    }

    protected async Task IndexDocumentAsync(
        EducationDocument document,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        ISearchProvider searchProvider = scope.ServiceProvider.GetRequiredService<ISearchProvider>();

        var result = await searchProvider.UpsertAsync(
            CollectionNames.EDUCATION_SEARCH,
            document,
            cancellationToken);
        if (result.IsFailure)
            throw result.Error.ToException();
    }

    protected async Task<SearchResponse<EducationDocumentDto>> SearchAsync(
        SearchRequest request,
        Action<UserScopedData>? configureUser = null,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserScopedData user = scope.ServiceProvider.GetRequiredService<UserScopedData>();
        configureUser?.Invoke(user);

        GetDocumentsHandler handler = scope.ServiceProvider.GetRequiredService<GetDocumentsHandler>();
        var result = await handler.Handle(new GetDocumentsQuery(request), cancellationToken);

        if (result.IsFailure)
            throw result.Error.ToException();

        return result.Value;
    }

    protected async Task<CSharpFunctionalExtensions.Result<SearchResponse<EducationDocumentDto>, SharedKernel.Error>>
        ExecuteSearchAsync(
            SearchRequest request,
            Action<UserScopedData>? configureUser = null,
            CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserScopedData user = scope.ServiceProvider.GetRequiredService<UserScopedData>();
        configureUser?.Invoke(user);

        GetDocumentsHandler handler = scope.ServiceProvider.GetRequiredService<GetDocumentsHandler>();
        return await handler.Handle(new GetDocumentsQuery(request), cancellationToken);
    }

    protected async Task RunFullReindexAsync(
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<FullSearchReindexRequestedHandler>()
            .Handle(new FullSearchReindexRequested(Guid.CreateVersion7(), DateTime.UtcNow), cancellationToken);
    }

    protected async Task RunPartialReindexAsync(
        EntityType entityType,
        CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        switch (entityType)
        {
            case EntityType.Course:
                await scope.ServiceProvider
                    .GetRequiredService<CoursesSearchReindexRequestedHandler>()
                    .Handle(new CoursesSearchReindexRequested(Guid.CreateVersion7(), DateTime.UtcNow), cancellationToken);
                break;
            case EntityType.Module:
                await scope.ServiceProvider
                    .GetRequiredService<ModulesSearchReindexRequestedHandler>()
                    .Handle(new ModulesSearchReindexRequested(Guid.CreateVersion7(), DateTime.UtcNow), cancellationToken);
                break;
            case EntityType.Project:
                await scope.ServiceProvider
                    .GetRequiredService<ProjectsSearchReindexRequestedHandler>()
                    .Handle(new ProjectsSearchReindexRequested(Guid.CreateVersion7(), DateTime.UtcNow), cancellationToken);
                break;
            case EntityType.Material:
                await scope.ServiceProvider
                    .GetRequiredService<MaterialsSearchReindexRequestedHandler>()
                    .Handle(new MaterialsSearchReindexRequested(Guid.CreateVersion7(), DateTime.UtcNow), cancellationToken);
                break;
            case EntityType.Issue:
                await scope.ServiceProvider
                    .GetRequiredService<IssuesSearchReindexRequestedHandler>()
                    .Handle(new IssuesSearchReindexRequested(Guid.CreateVersion7(), DateTime.UtcNow), cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entityType), entityType, null);
        }
    }

    protected async Task SeedEnrolledCourseAsync(Guid userId, Guid courseId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IConnectionMultiplexer redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        await redis.GetDatabase().SetAddAsync(
            EntitlementKeys.UserGrants(userId),
            [GrantTags.Course(courseId), GrantTags.CourseTrial(courseId)]);
    }

    protected async Task SeedTrialCourseAsync(Guid userId, Guid courseId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IConnectionMultiplexer redis = scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>();
        await redis.GetDatabase().SetAddAsync(EntitlementKeys.UserGrants(userId), GrantTags.CourseTrial(courseId));
    }

    protected async Task SeedTagAsync(Guid tagId, string title)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MockTagServiceClient mock = scope.ServiceProvider.GetRequiredService<MockTagServiceClient>();
        mock.Seed(tagId, title);
    }

    protected async Task RemoveTagAsync(Guid tagId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MockTagServiceClient mock = scope.ServiceProvider.GetRequiredService<MockTagServiceClient>();
        mock.Remove(tagId);
    }

    protected async Task SeedEntityTagsAsync(EntityType entityType, Guid entityId, params Guid[] tagIds)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        MockTagServiceClient mock = scope.ServiceProvider.GetRequiredService<MockTagServiceClient>();
        mock.SeedEntityTags(entityType, entityId, tagIds);
    }

    protected async Task ConfigureExportDocumentsAsync<TExport>(
        EntityType entityType,
        params TExport[] documents)
        where TExport : class
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IEducationContentServiceClient mock = scope.ServiceProvider.GetRequiredService<IEducationContentServiceClient>();
        mock.ConfigureExportDocuments(entityType, documents);
    }

    protected async Task SeedCollectionLookupAsync(CollectionSearchLookupDto dto)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IEducationContentServiceClient mock = scope.ServiceProvider.GetRequiredService<IEducationContentServiceClient>();
        mock.SeedCollectionLookup(dto);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _resetDatabase();
    }
}
