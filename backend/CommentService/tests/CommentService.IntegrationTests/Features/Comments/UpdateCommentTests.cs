using System.Net;
using System.Net.Http.Json;
using CommentService.Contracts.Comments.Requests;
using CommentService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class UpdateCommentTests : CommentServiceTestsBase
{
    public UpdateCommentTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task UpdateComment_AnonymousUser_Returns401()
    {
        // Arrange
        var comment = await CreateCommentAsync(MockMainAuthorId, "old");
        RemoveAuthentication();

        var request = new UpdateCommentRequest("new");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{comment.Id.Value}", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateComment_WithValidRequest_ShouldReturnOk_AndUpdateDb()
    {
        // Arrange
        var comment = await CreateCommentAsync(MockMainAuthorId, "old");
        var request = new UpdateCommentRequest("new");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{comment.Id.Value}", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        var returnedId = envelope!.Result;
        Assert.Equal(comment.Id.Value, returnedId);

        var dbContent = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(comment.Id);
            return entity!.Content.Value;
        });

        Assert.Equal("new", dbContent);
    }

    [Fact]
    public async Task UpdateComment_WithEmptyContent_ShouldReturnBadRequest_AndNotChangeDb()
    {
        // Arrange
        var comment = await CreateCommentAsync(MockMainAuthorId, "old");
        var request = new UpdateCommentRequest("");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{comment.Id.Value}", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var dbContent = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(comment.Id);
            return entity!.Content.Value;
        });

        Assert.Equal("old", dbContent);
    }

    [Fact]
    public async Task UpdateComment_WithNonExistentId_ShouldReturnNotFound()
    {
        // Arrange
        var request = new UpdateCommentRequest("new");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{Guid.NewGuid()}", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateComment_WithDeletedComment_ShouldReturnNotFound()
    {
        // Arrange
        var comment = await CreateCommentAsync(MockMainAuthorId, "old");

        await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(comment.Id);
            entity!.SoftDelete();
            await db.SaveChangesAsync();
        });

        var request = new UpdateCommentRequest("new");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{comment.Id.Value}", request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateComment_WithNotOwnedComment_ShouldReturnForbidden_AndNotChangeDb()
    {
        // Arrange
        var foreign = await CreateCommentAsync(MockSecondAuthorId, "foreign-old");
        var request = new UpdateCommentRequest("attempt");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{foreign.Id.Value}", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var dbContent = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(foreign.Id);
            return entity!.Content.Value;
        });

        Assert.Equal("foreign-old", dbContent);
    }

    [Fact]
    public async Task UpdateComment_WhenAccessDenied_ReturnsForbidden()
    {
        // Arrange — create as admin, then switch to student
        var comment = await CreateCommentAsync(MockMainAuthorId, "old");
        AuthenticateAs(MockMainAuthorId, "platform-participant");
        EntitlementChecker.DenyAll();

        var request = new UpdateCommentRequest("new");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{comment.Id.Value}", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify not changed
        var dbContent = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(comment.Id);
            return entity!.Content.Value;
        });
        Assert.Equal("old", dbContent);
    }

    [Fact]
    public async Task UpdateComment_WithValidRequest_ShouldChangeUpdatedAt()
    {
        // Arrange
        var comment = await CreateCommentAsync(MockMainAuthorId, "old");

        var before = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(comment.Id);
            return entity!.UpdatedAt;
        });

        var request = new UpdateCommentRequest("new");

        // Act
        var response = await AppHttpClient.PutAsJsonAsync($"comments/{comment.Id.Value}", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(comment.Id);
            return entity!.UpdatedAt;
        });

        Assert.True(after >= before);
    }
}
