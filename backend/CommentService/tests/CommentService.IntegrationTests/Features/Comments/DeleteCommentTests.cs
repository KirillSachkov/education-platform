using System.Net;
using System.Net.Http.Json;
using CommentService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Ownership;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class DeleteCommentTests : CommentServiceTestsBase
{
    public DeleteCommentTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task DeleteComment_AnonymousUser_Returns401()
    {
        // Arrange
        var root = await CreateCommentAsync(MockMainAuthorId, "root");
        RemoveAuthentication();

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{root.Id.Value}");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_WithValidId_ShouldReturnOk_AndSoftDelete()
    {
        // Arrange
        CancellationToken cancellationToken = new CancellationTokenSource().Token;
        
        var root = await CreateCommentAsync(MockMainAuthorId, "root");

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{root.Id.Value}", cancellationToken: cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>(cancellationToken: cancellationToken);
        var returnedId = envelope!.Result;
        Assert.Equal(root.Id.Value, returnedId);

        var isDeleted = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == root.Id, cancellationToken: cancellationToken);
            return entity!.IsDeleted;
        });

        Assert.True(isDeleted); // handler: comment.SoftDelete() [file:1]
    }

    [Fact]
    public async Task DeleteComment_WhenAccessDenied_ReturnsForbidden()
    {
        // Arrange — create as admin, then switch to student
        var root = await CreateCommentAsync(MockMainAuthorId, "root");
        AuthenticateAs(MockMainAuthorId, "platform-participant");
        EntitlementChecker.DenyAll();

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{root.Id.Value}");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Verify not deleted
        var isDeleted = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(root.Id);
            return entity!.IsDeleted;
        });
        Assert.False(isDeleted);
    }

    [Fact]
    public async Task DeleteComment_WithDeletedComment_ShouldReturnNotFound()
    {
        // Arrange
        CancellationToken cancellationToken = new CancellationTokenSource().Token;
        
        var root = await CreateCommentAsync(MockMainAuthorId, "root");

        await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FindAsync(root.Id);
            entity!.SoftDelete();
            await db.SaveChangesAsync(cancellationToken);
        });

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{root.Id.Value}", cancellationToken: cancellationToken);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_WithNotOwnedComment_ShouldReturnForbidden_AndNotDelete()
    {
        // Arrange
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        var foreign = await CreateCommentAsync(MockSecondAuthorId, "foreign");

        // Switch to non-admin user — admin can moderate any comment
        AuthenticateAs(MockMainAuthorId, "platform-participant");

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{foreign.Id.Value}", cancellationToken: cancellationToken);

        // Assert — handler returns 403 when caller is neither owner, moderator, nor course author.
        // (Pre-audit: returned 404 as an opaque response. Post-audit: 403 is correct — existence
        // is already revealed by the entitlement check, so returning 403 is more accurate and
        // consistent with the ownership-fail path.)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var isDeleted = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments.FirstOrDefaultAsync(x => x.Id == foreign.Id, cancellationToken);
            return entity!.IsDeleted;
        });

        Assert.False(isDeleted);
    }

    [Fact]
    public async Task DeleteComment_AsModerator_BypassesOwnershipCheck()
    {
        // Moderator (Comments.MODERATE permission) deletes a comment they didn't author
        // и не являются автором контента — bypass через canModerate в handler'е.
        var foreign = await CreateCommentAsync(MockSecondAuthorId, "abusive comment");

        Guid moderatorId = Guid.NewGuid();
        AuthenticateAs(moderatorId, "platform-moderator");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"comments/{foreign.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        bool isDeleted = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == foreign.Id);
            return entity!.IsDeleted;
        });
        Assert.True(isDeleted);
    }

    [Fact]
    public async Task DeleteComment_AsCourseAuthor_BypassesOwnershipCheck()
    {
        // Автор курса (resolved через ECS GetEntityOwnership) удаляет коммент, не являясь
        // автором коммента и не имея Comments.MODERATE — bypass через ownership lookup.
        var foreign = await CreateCommentAsync(MockSecondAuthorId, "comment on my course");

        Guid courseAuthorId = Guid.NewGuid();
        EcsClient.GetEntityOwnershipAsync(
                Arg.Any<string>(), Arg.Is(foreign.EntityReference.Id), Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(Guid.NewGuid(), courseAuthorId)));

        AuthenticateAs(courseAuthorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"comments/{foreign.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        bool isDeleted = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == foreign.Id);
            return entity!.IsDeleted;
        });
        Assert.True(isDeleted);
    }
}
