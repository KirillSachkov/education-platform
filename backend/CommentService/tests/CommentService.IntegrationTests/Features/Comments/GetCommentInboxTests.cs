using System.Net;
using System.Net.Http.Json;
using CommentService.Contracts.Comments.Dtos;
using CommentService.IntegrationTests.Infrastructure;
using Common;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Materials;
using NSubstitute;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetCommentInboxTests : CommentServiceTestsBase
{
    public GetCommentInboxTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Inbox_WhenMaterialBoundToCourse_ReturnsTargetCourseSlug()
    {
        // Arrange — два материала: один привязан к курсу, второй standalone.
        Guid boundMaterialId = Guid.NewGuid();
        Guid standaloneMaterialId = Guid.NewGuid();

        var parentBound = await CreateCommentAsync(
            authorId: MockMainAuthorId,
            content: "родительский коммент на bound-материал",
            parentId: null,
            targetEntityId: boundMaterialId);

        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "ответ на bound-материал",
            parentId: parentBound.Id.Value,
            targetEntityId: boundMaterialId);

        var parentStandalone = await CreateCommentAsync(
            authorId: MockMainAuthorId,
            content: "родительский коммент на standalone-материал",
            parentId: null,
            targetEntityId: standaloneMaterialId);

        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "ответ на standalone-материал",
            parentId: parentStandalone.Id.Value,
            targetEntityId: standaloneMaterialId);

        // ECS возвращает binding только для одного из материалов.
        EcsClient.GetMaterialCourseBindingsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>(
                new[]
                {
                    new MaterialCourseBindingLookupDto(boundMaterialId, Guid.NewGuid(), "intro-to-csharp"),
                }));

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/inbox?limit=20");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<InboxCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<InboxCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Equal(2, envelope.Result.Count);

        InboxCommentDto bound = envelope.Result.Single(i => i.TargetEntityId == boundMaterialId);
        Assert.Equal("intro-to-csharp", bound.TargetCourseSlug);

        InboxCommentDto standalone = envelope.Result.Single(i => i.TargetEntityId == standaloneMaterialId);
        Assert.Null(standalone.TargetCourseSlug);
    }

    [Fact]
    public async Task Inbox_WhenIssueBoundToCourse_ReturnsTargetCourseSlug()
    {
        // Arrange — комментарий на задачу, ECS отдаёт course-binding для неё.
        Guid issueId = Guid.NewGuid();

        var parent = await CreateCommentAsync(
            authorId: MockMainAuthorId,
            content: "родительский коммент на задачу",
            parentId: null,
            targetEntityId: issueId,
            targetEntityType: EntityType.Issue);

        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "ответ на задачу",
            parentId: parent.Id.Value,
            targetEntityId: issueId,
            targetEntityType: EntityType.Issue);

        EcsClient.GetIssueCourseBindingsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<IssueCourseBindingLookupDto>, Error>(
                new[]
                {
                    new IssueCourseBindingLookupDto(issueId, Guid.NewGuid(), "csharp-basics"),
                }));

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/inbox?limit=20");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<InboxCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<InboxCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Single(envelope.Result);
        Assert.Equal(issueId, envelope.Result[0].TargetEntityId);
        Assert.Equal("issue", envelope.Result[0].TargetEntityType);
        Assert.Equal("csharp-basics", envelope.Result[0].TargetCourseSlug);
    }

    [Fact]
    public async Task Inbox_WhenEcsFails_GracefullyReturnsItemsWithoutCourseSlug()
    {
        // Arrange — единственный коммент, ECS падает.
        Guid materialId = Guid.NewGuid();
        var parent = await CreateCommentAsync(
            authorId: MockMainAuthorId,
            content: "родительский",
            parentId: null,
            targetEntityId: materialId);

        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: "ответ",
            parentId: parent.Id.Value,
            targetEntityId: materialId);

        EcsClient.GetMaterialCourseBindingsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>(
                Error.Failure("ecs.unavailable", "ECS недоступен")));

        AuthenticateAs(MockMainAuthorId, "platform-participant");

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync("/comments/inbox?limit=20");

        // Assert — endpoint всё ещё OK, items без TargetCourseSlug.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<InboxCommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<InboxCommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Single(envelope.Result);
        Assert.Null(envelope.Result[0].TargetCourseSlug);
    }
}
