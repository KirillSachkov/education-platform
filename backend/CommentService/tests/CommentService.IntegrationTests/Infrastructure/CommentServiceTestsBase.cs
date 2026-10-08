using Common;
using System.Net.Http.Headers;
using CommentService.Domain;
using CommentService.Infrastructure.Postgres;
using ContentAccess.TestSupport;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine.Testing;

namespace CommentService.IntegrationTests.Infrastructure;

[Collection(nameof(IntegrationTestsFixture))]
public abstract class CommentServiceTestsBase : IAsyncLifetime
{
    public static readonly Guid MockMainAuthorId = Guid.Parse("11111111-1111-1111-1111-111112111111");
    public static readonly Guid MockSecondAuthorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly EntityType TestTargetEntityType = EntityType.Material;
    public static readonly Guid TestTargetEntityId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly Func<Task> _resetDatabase;

    protected CommentServiceTestsBase(IntegrationTestsWebFactory factory)
    {
        EntitlementChecker = factory.EntitlementChecker;
        EcsClient = factory.EcsClient;
        OutboxCollector = factory.OutboxCollector;
        AppHttpClient = factory.CreateClient();
        Services = factory.Services;
        Host = factory.Services.GetRequiredService<IHost>();
        _resetDatabase = factory.ResetDatabaseAsync;
    }

    protected IServiceProvider Services { get; init; }

    protected IHost Host { get; init; }

    protected FakeEntitlementChecker EntitlementChecker { get; init; }

    protected IEducationContentServiceClient EcsClient { get; init; }

    protected TestOutboxCollector OutboxCollector { get; init; }

    protected HttpClient AppHttpClient { get; init; }

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

    protected async Task<Comment> CreateCommentAsync(
        Guid authorId,
        string content,
        Guid? parentId,
        Guid? targetAuthorId = null,
        Guid? targetEntityId = null,
        EntityType? targetEntityType = null,
        CancellationToken cancellationToken = default)
    {
        Comment comment = await ExecuteInDb(async dbContext =>
        {
            Guid effectiveTargetId = targetEntityId ?? TestTargetEntityId;
            EntityType effectiveTargetType = targetEntityType ?? TestTargetEntityType;
            Guid effectiveTargetAuthorId = targetAuthorId ?? authorId;
            await SeedTargetOwnershipAsync(
                dbContext,
                effectiveTargetType,
                effectiveTargetId,
                effectiveTargetAuthorId,
                cancellationToken);

            CommentEntityReference entityReference =
                CommentEntityReference.Of(
                    effectiveTargetType,
                    effectiveTargetId).Value;
            Content contentComment = Content.Of(content).Value;
            Comment? resultComment;

            if (parentId == null)
            {
                CommentId commentId = CommentId.Create();
                resultComment = Comment.CreateParent(
                    authorId, entityReference, contentComment, commentId, targetAuthorId);
            }
            else
            {
                CommentId commentId = CommentId.Create();

                Comment? parentComment =
                    await dbContext.Comments.FirstOrDefaultAsync(x => x.Id == CommentId.Of(parentId.Value) && !x.IsDeleted,
                        cancellationToken);

                resultComment = Comment.CreateChild(
                    parentComment!, authorId, contentComment, commentId, targetAuthorId);
            }

            await dbContext.Comments.AddAsync(resultComment, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            return resultComment;
        });

        return comment;
    }

    protected Task<Comment> CreateCommentAsync(Guid authorId, string content)
        => CreateCommentAsync(authorId, content, null);

    private static Task SeedTargetOwnershipAsync(
        CommentDbContext dbContext,
        EntityType entityType,
        Guid entityId,
        Guid authorId,
        CancellationToken cancellationToken) =>
        entityType switch
        {
            EntityType.Course => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO education.courses (id, author_id)
                VALUES ({entityId}, {authorId})
                ON CONFLICT (id) DO NOTHING;
                """, cancellationToken),
            EntityType.Material => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO education.materials (id, author_id)
                VALUES ({entityId}, {authorId})
                ON CONFLICT (id) DO NOTHING;
                """, cancellationToken),
            EntityType.Issue => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO education.issues (id, author_id, project_id)
                VALUES ({entityId}, {authorId}, {entityId})
                ON CONFLICT (id) DO NOTHING;
                """, cancellationToken),
            EntityType.Quiz => dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO education.quizzes (id, author_id)
                VALUES ({entityId}, {authorId})
                ON CONFLICT (id) DO NOTHING;
                """, cancellationToken),
            _ => Task.CompletedTask,
        };

    protected async Task ExecuteInDb(Func<CommentDbContext, Task> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        CommentDbContext dbContext = scope.ServiceProvider.GetRequiredService<CommentDbContext>();
        await action(dbContext);
    }

    protected async Task<T> ExecuteInDb<T>(Func<CommentDbContext, Task<T>> action)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        CommentDbContext dbContext = scope.ServiceProvider.GetRequiredService<CommentDbContext>();
        return await action(dbContext);
    }

    public async Task InitializeAsync()
    {
        await _resetDatabase();
        AuthenticateAsAdmin(MockMainAuthorId);
    }

    public Task DisposeAsync()
    {
        RemoveAuthentication();
        return Task.CompletedTask;
    }
}
