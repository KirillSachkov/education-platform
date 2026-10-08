using System.Net;
using System.Net.Http.Json;
using Common;
using CommentService.Contracts.Comments.Requests;
using CommentService.Domain;
using CommentService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Ownership;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CreateCommentTests : CommentServiceTestsBase
{
    public CreateCommentTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateComment_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();
        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Тестовый комментарий",
            null);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WithValidParentComment_ShouldReturnSuccess()
    {
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Это тестовый комментарий",
            null);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotEqual(Guid.Empty, envelope.Result);
    }

    [Fact]
    public async Task CreateComment_WithValidChildComment_ShouldReturnSuccess()
    {
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        Comment parentComment = await CreateCommentAsync(MockMainAuthorId, "Родительский комментарий");

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Дочерний комментарий",
            parentComment.Id.Value);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotEqual(Guid.Empty, envelope.Result);
    }

    [Fact]
    public async Task CreateComment_ReplyUsesCurrentOwnershipInsteadOfStaleParentSnapshot()
    {
        Guid oldOwnerId = Guid.CreateVersion7();
        Guid currentOwnerId = Guid.CreateVersion7();
        Comment parent = await CreateCommentAsync(
            MockSecondAuthorId,
            "Родитель со старым owner snapshot",
            parentId: null,
            targetAuthorId: oldOwnerId);
        EcsClient.GetEntityOwnershipAsync(
                "material",
                TestTargetEntityId,
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(null, currentOwnerId)));

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Ответ после смены владельца",
            parent.Id.Value);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Guid? targetAuthorId = await ExecuteInDb(db => db.Comments
            .Where(x => x.Id == CommentId.Of(envelope.Result))
            .Select(x => x.TargetAuthorId)
            .SingleAsync());
        Assert.Equal(currentOwnerId, targetAuthorId);
    }

    [Fact]
    public async Task CreateComment_WithParentFromDifferentTarget_ShouldReturnBadRequest_AndNotCreateReply()
    {
        Comment parentComment = await CreateCommentAsync(MockMainAuthorId, "Родительский комментарий");
        Guid differentMaterialId = Guid.CreateVersion7();

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, differentMaterialId),
            "Попытка cross-target reply",
            parentComment.Id.Value);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        int commentsCount = await ExecuteInDb(db => db.Comments.CountAsync());
        Assert.Equal(1, commentsCount);
    }

    [Fact]
    public async Task CreateComment_WithNonExistentParent_ShouldReturnError()
    {
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Дочерний комментарий",
            Guid.NewGuid());

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WithEmptyContent_ShouldReturnValidationError()
    {
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            string.Empty,
            null);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WithUnsupportedTargetEntityType_ShouldReturnValidationError()
    {
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        var request = new CreateCommentRequest(
            new EntityReferenceDto(EntityType.Module, TestTargetEntityId),
            "Тестовый комментарий",
            null);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WithDeletedParent_ShouldReturnError()
    {
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        Comment parentComment = await CreateCommentAsync(MockMainAuthorId, "Родительский комментарий");

        await ExecuteInDb(async db =>
        {
            Comment comment = await db.Comments.FirstAsync(c => c.Id == parentComment.Id, cancellationToken: cancellationToken);
            comment.SoftDelete();
            await db.SaveChangesAsync(cancellationToken);
        });

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Дочерний комментарий",
            parentComment.Id.Value);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WhenAccessDenied_ReturnsForbidden()
    {
        AuthenticateAs(MockMainAuthorId, "platform-participant");
        EntitlementChecker.DenyAll();

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Тестовый комментарий",
            null);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_WhenAccessGranted_ReturnsSuccess()
    {
        AuthenticateAs(MockMainAuthorId, "platform-participant");
        EntitlementChecker.GrantAll();

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Тестовый комментарий",
            null);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_AdminBypassesEntitlementCheck()
    {
        AuthenticateAs(MockMainAuthorId, "platform-admin");
        EntitlementChecker.DenyAll();

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Admin комментарий",
            null);

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_MultipleChildComments_ShouldCreateCorrectHierarchy()
    {
        CancellationToken cancellationToken = new CancellationTokenSource().Token;

        Comment parentComment = await CreateCommentAsync(MockMainAuthorId, "Родительский комментарий");

        var request1 = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Первый дочерний комментарий",
            parentComment.Id.Value);

        var request2 = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Второй дочерний комментарий",
            parentComment.Id.Value);

        var response1 = await AppHttpClient.PostAsJsonAsync("/comments", request1, cancellationToken);
        var response2 = await AppHttpClient.PostAsJsonAsync("/comments", request2, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var envelope1 = await response1.Content.ReadFromJsonAsync<Envelope<Guid>>();
        var envelope2 = await response2.Content.ReadFromJsonAsync<Envelope<Guid>>();

        Assert.NotNull(envelope1);
        Assert.NotNull(envelope2);
        Assert.False(envelope1.IsError);
        Assert.False(envelope2.IsError);
        Assert.NotEqual(Guid.Empty, envelope1.Result);
        Assert.NotEqual(Guid.Empty, envelope2.Result);
    }

    [Fact]
    public async Task ExistingLowercaseTargetEntityType_ShouldMaterializeAfterRefactor()
    {
        Guid commentId = Guid.CreateVersion7();
        Guid authorId = Guid.CreateVersion7();

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO comments.comments (
                    id,
                    author_id,
                    target_entity_type,
                    target_entity_id,
                    content,
                    path,
                    depth,
                    is_deleted,
                    created_at,
                    updated_at
                )
                VALUES (
                    {0},
                    {1},
                    {2},
                    {3},
                    {4},
                    CAST({5} AS ltree),
                    {6},
                    false,
                    timezone('utc', now()),
                    timezone('utc', now())
                );
                """,
                commentId,
                authorId,
                "material",
                TestTargetEntityId,
                "legacy comment",
                commentId.ToString(),
                0);
        });

        Comment comment = await ExecuteInDb(async db =>
            await db.Comments.SingleAsync(x => x.Id == CommentId.Of(commentId)));

        Assert.Equal(EntityType.Material, comment.EntityReference.Type);
        Assert.Equal(TestTargetEntityId, comment.EntityReference.Id);
    }
}
