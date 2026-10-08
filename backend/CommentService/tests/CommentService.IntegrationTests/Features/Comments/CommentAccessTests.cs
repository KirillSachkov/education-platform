using System.Net;
using System.Net.Http.Json;
using Common;
using CommentService.Contracts.Comments.Requests;
using CommentService.Domain;
using ContentAccess;
using CommentService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Ownership;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CommentAccessTests : CommentServiceTestsBase
{
    private static readonly Guid _courseId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid _courseAuthorId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public CommentAccessTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateComment_NotEnrolled_OnCourseEntity_ShouldReturn403()
    {
        // Arrange: entity belongs to a course, user is participant but not enrolled.
        // FakeEntitlementChecker grants material-level access (PUBLIC reason triggers enrollment gate)
        // but denies course-level access via DenyResourceType, simulating an unenrolled user.
        AuthenticateAs(MockMainAuthorId, "platform-participant");
        SetupEcsOwnership(_courseId, _courseAuthorId);

        EntitlementChecker.GrantAll();
        EntitlementChecker.DenyResourceType("course");

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Комментарий без записи",
            null);

        // Act
        var response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_PublicCourseBoundQuiz_WhenNotEnrolled_Returns403()
    {
        Guid quizId = Guid.CreateVersion7();
        AuthenticateAs(MockMainAuthorId, "platform-participant");
        EntitlementChecker.SetDecision(
            "quiz",
            quizId,
            AccessDecision.Granted(AccessReason.PUBLIC));
        EntitlementChecker.SetDecision(
            "course",
            _courseId,
            AccessDecision.Granted(AccessReason.PUBLIC));
        SetupEcsOwnership(_courseId, _courseAuthorId);

        var request = new CreateCommentRequest(
            new EntityReferenceDto(EntityType.Quiz, quizId),
            "Комментарий к квизу без записи",
            null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_PublicCourseBoundQuiz_WhenEnrolled_ReturnsSuccess()
    {
        Guid quizId = Guid.CreateVersion7();
        AuthenticateAs(MockMainAuthorId, "platform-participant");
        EntitlementChecker.SetDecision(
            "quiz",
            quizId,
            AccessDecision.Granted(AccessReason.PUBLIC));
        EntitlementChecker.SetDecision(
            "course",
            _courseId,
            AccessDecision.Granted(AccessReason.ENTITLEMENT));
        SetupEcsOwnership(_courseId, _courseAuthorId);

        var request = new CreateCommentRequest(
            new EntityReferenceDto(EntityType.Quiz, quizId),
            "Комментарий к квизу после записи",
            null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_CourseOwner_BypassesDeniedEntitlement()
    {
        AuthenticateAs(_courseAuthorId, "platform-author");
        EntitlementChecker.DenyAll();
        SetupEcsOwnership(_courseId, _courseAuthorId);

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Ответ автора курса",
            null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CreateComment_SharedContentManager_BypassesEnrollmentGate()
    {
        Guid collaboratorId = Guid.CreateVersion7();
        AuthenticateAs(collaboratorId, "platform-author");
        EntitlementChecker.SetDecision(
            "material",
            TestTargetEntityId,
            AccessDecision.Granted(AccessReason.PUBLIC));
        EntitlementChecker.SetDecision(
            "course",
            _courseId,
            AccessDecision.Granted(AccessReason.PUBLIC));
        EcsClient.GetEntityOwnershipAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(
                    _courseId,
                    _courseAuthorId,
                    collaboratorId,
                    [_courseAuthorId, collaboratorId])));

        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Ответ соавтора shared-контента",
            null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_CourseAuthorCanDeleteOthersComment()
    {
        // Arrange: create a comment by MockSecondAuthorId
        var comment = await CreateCommentAsync(MockSecondAuthorId, "чужой комментарий");

        // Switch to course author (not admin, not comment owner)
        AuthenticateAs(_courseAuthorId, "platform-author");
        SetupEcsOwnership(_courseId, _courseAuthorId);

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{comment.Id.Value}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        bool isDeleted = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == comment.Id);
            return entity!.IsDeleted;
        });

        Assert.True(isDeleted);
    }

    [Fact]
    public async Task DeleteComment_AdminCanDeleteAnyComment()
    {
        // Arrange: create a comment by MockSecondAuthorId
        var comment = await CreateCommentAsync(MockSecondAuthorId, "админ удалит");

        // Admin user (different from comment author)
        AuthenticateAsAdmin(MockMainAuthorId);

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{comment.Id.Value}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        bool isDeleted = await ExecuteInDb(async db =>
        {
            var entity = await db.Comments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == comment.Id);
            return entity!.IsDeleted;
        });

        Assert.True(isDeleted);
    }

    [Fact]
    public async Task DeleteComment_NonOwnerNonAuthor_ShouldReturnForbidden()
    {
        // Arrange: comment by second author, authenticated as participant (not course author)
        var comment = await CreateCommentAsync(MockSecondAuthorId, "не мой комментарий");

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        // ECS returns a course, but AuthorId is someone else
        SetupEcsOwnership(_courseId, Guid.NewGuid());

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{comment.Id.Value}");

        // Assert — ECS confirms the comment exists but caller is not the course author → 403.
        // (Pre-audit: returned 404 as an opaque response. Post-audit: 403 is correct — the
        // ownership check ran to completion and explicitly denied the caller.)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_EntitlementDenied_ShouldReturn403()
    {
        // Arrange: comment by main author, entitlement denied
        var comment = await CreateCommentAsync(MockMainAuthorId, "мой комментарий");

        AuthenticateAs(MockMainAuthorId, "platform-participant");
        EntitlementChecker.DenyAll();

        // Act
        var response = await AppHttpClient.DeleteAsync($"comments/{comment.Id.Value}");

        // Assert — entitlement check fires first (before ownership)
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private void SetupEcsOwnership(Guid courseId, Guid authorId)
    {
        EcsClient.GetEntityOwnershipAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<EntityOwnershipDto, Error>(
                new EntityOwnershipDto(courseId, authorId)));
    }
}
