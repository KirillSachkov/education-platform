using System.Net;
using System.Net.Http.Json;
using CommentService.Contracts;
using CommentService.Contracts.Comments.Dtos;
using CommentService.Domain;
using CommentService.IntegrationTests.Infrastructure;
using Common;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class AuthorFeedTests : CommentServiceTestsBase
{
    public AuthorFeedTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AuthorFeed_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthorFeed_ReturnsOnlyCommentsOnOwnContent()
    {
        // Arrange — два коммента под контентом MockMainAuthorId, один под контентом другого автора.
        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "коммент под моим материалом 1",
            parentId: null,
            targetAuthorId: MockMainAuthorId);

        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "коммент под чужим материалом",
            parentId: null,
            targetAuthorId: MockSecondAuthorId,
            targetEntityId: Guid.NewGuid());

        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "коммент под моим материалом 2",
            parentId: null,
            targetAuthorId: MockMainAuthorId,
            targetEntityId: Guid.NewGuid());

        // Authenticate as content-author via NON-admin token, чтобы Tier 2 фильтр сработал.
        AuthenticateAs(MockMainAuthorId, "platform-participant");

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<CursorResponse<AuthorFeedCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        Assert.Equal(2, envelope.Result.Items.Count);
        Assert.All(envelope.Result.Items, item =>
        {
            Assert.Contains("моим материалом", item.Content, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task AuthorFeed_MaterialAttachSwitchesAccessToCourseOwnerImmediately()
    {
        Guid materialId = Guid.CreateVersion7();
        Guid directAuthorId = Guid.CreateVersion7();
        Guid courseOwnerId = Guid.CreateVersion7();
        Guid courseId = Guid.CreateVersion7();
        await CreateCommentAsync(
            MockSecondAuthorId,
            "material attach",
            null,
            directAuthorId,
            materialId,
            EntityType.Material);

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO education.courses (id, author_id) VALUES ({courseId}, {courseOwnerId});
                INSERT INTO education.course_materials (course_id, material_id)
                VALUES ({courseId}, {materialId});
                """);
        });

        Assert.Empty((await GetFeedAsAsync(directAuthorId)).Items);
        Assert.Single((await GetFeedAsAsync(courseOwnerId)).Items);
    }

    [Fact]
    public async Task AuthorFeed_QuizDetachSwitchesAccessBackToDirectAuthorImmediately()
    {
        Guid quizId = Guid.CreateVersion7();
        Guid directAuthorId = Guid.CreateVersion7();
        Guid courseOwnerId = Guid.CreateVersion7();
        Guid courseId = Guid.CreateVersion7();
        await CreateCommentAsync(
            MockSecondAuthorId,
            "quiz detach",
            null,
            directAuthorId,
            quizId,
            EntityType.Quiz);

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO education.courses (id, author_id) VALUES ({courseId}, {courseOwnerId});
                INSERT INTO education.course_quizzes (course_id, quiz_id)
                VALUES ({courseId}, {quizId});
                """);
        });
        Assert.Single((await GetFeedAsAsync(courseOwnerId)).Items);

        await ExecuteInDb(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM education.course_quizzes
            WHERE course_id = {courseId} AND quiz_id = {quizId};
            """));

        Assert.Empty((await GetFeedAsAsync(courseOwnerId)).Items);
        Assert.Single((await GetFeedAsAsync(directAuthorId)).Items);
    }

    [Fact]
    public async Task AuthorFeed_IssueModuleTransferSwitchesAccessToDestinationCourseOwner()
    {
        Guid issueId = Guid.CreateVersion7();
        Guid moduleId = Guid.CreateVersion7();
        Guid sourceCourseId = Guid.CreateVersion7();
        Guid destinationCourseId = Guid.CreateVersion7();
        Guid sourceOwnerId = Guid.CreateVersion7();
        Guid destinationOwnerId = Guid.CreateVersion7();
        await CreateCommentAsync(
            MockSecondAuthorId,
            "issue transfer",
            null,
            sourceOwnerId,
            issueId,
            EntityType.Issue);

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO education.courses (id, author_id)
                VALUES ({sourceCourseId}, {sourceOwnerId}), ({destinationCourseId}, {destinationOwnerId});
                INSERT INTO education.course_items (course_id, item_type, reference_id)
                VALUES ({sourceCourseId}, 'Module', {moduleId});
                INSERT INTO education.module_items (module_id, item_type, reference_id)
                VALUES ({moduleId}, 'Issue', {issueId});
                """);
        });
        Assert.Single((await GetFeedAsAsync(sourceOwnerId)).Items);

        await ExecuteInDb(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE education.course_items
            SET course_id = {destinationCourseId}
            WHERE item_type = 'Module' AND reference_id = {moduleId};
            """));

        Assert.Empty((await GetFeedAsAsync(sourceOwnerId)).Items);
        Assert.Single((await GetFeedAsAsync(destinationOwnerId)).Items);
    }

    [Fact]
    public async Task AuthorFeed_WithoutReply_ExcludesCommentsWithAuthorReply()
    {
        // Two root comments. Author replies to one of them. WithoutReply=true → expect only the other.
        Comment commentWithReply = await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "первый коммент",
            parentId: null,
            targetAuthorId: MockMainAuthorId);

        await CreateCommentAsync(
            authorId: MockMainAuthorId,
            content: "ответ автора",
            parentId: commentWithReply.Id.Value,
            targetAuthorId: MockMainAuthorId);

        Comment commentWithoutReply = await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "второй коммент",
            parentId: null,
            targetAuthorId: MockMainAuthorId,
            targetEntityId: Guid.NewGuid());

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/?withoutReply=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<CursorResponse<AuthorFeedCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);

        Assert.Single(envelope.Result.Items);
        Assert.Equal(commentWithoutReply.Id.Value, envelope.Result.Items[0].Id);
        Assert.False(envelope.Result.Items[0].HasMyReply);
    }

    [Fact]
    public async Task AuthorFeed_HasMyReply_FlagComputedCorrectly()
    {
        Comment c1 = await CreateCommentAsync(
            MockSecondAuthorId, "первый", null, MockMainAuthorId);

        await CreateCommentAsync(
            MockMainAuthorId, "ответ автора", c1.Id.Value, MockMainAuthorId);

        Comment c2 = await CreateCommentAsync(
            MockSecondAuthorId, "второй без ответа", null, MockMainAuthorId, Guid.NewGuid());

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/");

        Envelope<CursorResponse<AuthorFeedCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);

        AuthorFeedCommentDto withReply = envelope.Result.Items.Single(i => i.Id == c1.Id.Value);
        AuthorFeedCommentDto withoutReply = envelope.Result.Items.Single(i => i.Id == c2.Id.Value);

        Assert.True(withReply.HasMyReply);
        Assert.False(withoutReply.HasMyReply);
    }

    [Fact]
    public async Task AuthorFeed_AdminBypass_SeesAllComments()
    {
        await CreateCommentAsync(
            MockSecondAuthorId, "под чужим", null, MockSecondAuthorId);
        await CreateCommentAsync(
            MockMainAuthorId, "под собой", null, MockMainAuthorId, Guid.NewGuid());

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/");

        Envelope<CursorResponse<AuthorFeedCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.Equal(2, envelope.Result!.Items.Count);
    }

    [Fact]
    public async Task AuthorFeed_NonAuthor_SeesEmptyFeed()
    {
        // Третий пользователь — не автор контента, не админ. Должен получить пустую ленту.
        await CreateCommentAsync(
            MockSecondAuthorId, "под основным автором", null, MockMainAuthorId);

        Guid otherUser = Guid.NewGuid();
        AuthenticateAs(otherUser, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/");

        Envelope<CursorResponse<AuthorFeedCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result.Items);
    }

    [Fact]
    public async Task AuthorFeed_KeysetPagination_WorksAcrossPages()
    {
        // Создаём 5 комментов, ленту запрашиваем по 2.
        for (int i = 0; i < 5; i++)
        {
            await CreateCommentAsync(
                MockSecondAuthorId, $"коммент {i}", null, MockMainAuthorId, Guid.NewGuid());
        }

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        HttpResponseMessage page1 = await AppHttpClient.GetAsync("/comments/author-feed/?limit=2");
        Envelope<CursorResponse<AuthorFeedCommentDto>>? env1 =
            await page1.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(env1);
        Assert.NotNull(env1.Result);
        Assert.Equal(2, env1.Result.Items.Count);
        Assert.NotNull(env1.Result.NextCursor);

        HttpResponseMessage page2 = await AppHttpClient.GetAsync(
            $"/comments/author-feed/?limit=2&cursor={Uri.EscapeDataString(env1.Result.NextCursor!)}");
        Envelope<CursorResponse<AuthorFeedCommentDto>>? env2 =
            await page2.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(env2);
        Assert.NotNull(env2.Result);
        Assert.Equal(2, env2.Result.Items.Count);

        // No overlap between pages.
        var idsP1 = env1.Result.Items.Select(i => i.Id).ToHashSet();
        var idsP2 = env2.Result.Items.Select(i => i.Id).ToHashSet();
        Assert.Empty(idsP1.Intersect(idsP2));
    }

    [Fact]
    public async Task AuthorFeed_UnreadOnly_FiltersByViewedAt()
    {
        // Создаём «старый» и «новый» комменты, потом раздвигаем их created_at и
        // фиксируем viewed_at между ними — детерминированно, без Task.Delay.
        Comment oldComment = await CreateCommentAsync(
            MockSecondAuthorId, "старый коммент", null, MockMainAuthorId);
        Comment newComment = await CreateCommentAsync(
            MockSecondAuthorId, "новый коммент", null, MockMainAuthorId, Guid.NewGuid());

        DateTime now = DateTime.UtcNow;
        DateTime oldAt = now.AddHours(-2);
        DateTime viewedAt = now.AddHours(-1);
        DateTime newAt = now;

        await ExecuteInDb(async dbContext =>
        {
            await dbContext.Comments
                .Where(c => c.Id == oldComment.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, oldAt));
            await dbContext.Comments
                .Where(c => c.Id == newComment.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CreatedAt, newAt));

            dbContext.AuthorFeedStates.Add(AuthorFeedState.Create(MockMainAuthorId, viewedAt));
            await dbContext.SaveChangesAsync();
        });

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/?unreadOnly=true");
        Envelope<CursorResponse<AuthorFeedCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);

        Assert.Single(envelope.Result.Items);
        Assert.Equal(newComment.Id.Value, envelope.Result.Items[0].Id);
        Assert.True(envelope.Result.Items[0].IsUnread);
    }

    [Fact]
    public async Task MarkViewed_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            "/comments/author-feed/mark-viewed/", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MarkViewed_IsIdempotent()
    {
        AuthenticateAs(MockMainAuthorId, "platform-participant");

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            "/comments/author-feed/mark-viewed/", content: null);
        HttpResponseMessage second = await AppHttpClient.PostAsync(
            "/comments/author-feed/mark-viewed/", content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Проверяем, что в БД ровно одна строка для этого автора.
        int count = await ExecuteInDb(async dbContext =>
            await dbContext.AuthorFeedStates
                .Where(s => s.AuthorId == MockMainAuthorId)
                .CountAsync());
        Assert.Equal(1, count);
    }

    private async Task<CursorResponse<AuthorFeedCommentDto>> GetFeedAsAsync(Guid userId)
    {
        AuthenticateAs(userId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/author-feed/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<CursorResponse<AuthorFeedCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<AuthorFeedCommentDto>>>();
        Assert.NotNull(envelope?.Result);
        return envelope.Result;
    }
}
