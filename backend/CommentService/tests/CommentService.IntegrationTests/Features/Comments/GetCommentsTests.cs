using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts.HttpCommunication;
using Common;
using CommentService.Contracts;
using CommentService.Contracts.Comments.Dtos;
using AuthUserLookupDto = AuthService.Contracts.AuthUserLookupDto;
using CommentService.Contracts.Comments.Requests;
using CommentService.Domain;
using CommentService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetCommentsTests : CommentServiceTestsBase
{
    public GetCommentsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetRoots_ShouldEnrichRootAndPreviewChildrenWithAuthorInfo()
    {
        // Regression: enricher mutated a temporary list, leaving roots/PreviewChildren un-enriched
        // → frontend rendered "Пользователь" fallback for everything on the discussion page.
        IAuthServiceClient authClient = Services.GetRequiredService<IAuthServiceClient>();
        authClient
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                IReadOnlyList<Guid> ids = ci.Arg<IReadOnlyList<Guid>>();
                var users = ids
                    .Select(id => new AuthUserLookupDto(
                        UserId: id,
                        Name: $"DisplayName-{id.ToString()[..8]}",
                        Username: $"user-{id.ToString()[..8]}",
                        Email: $"{id.ToString()[..8]}@test",
                        AvatarId: null))
                    .ToList();
                return Task.FromResult(
                    Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(users));
            });

        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root with replies");
        await CreateCommentAsync(MockSecondAuthorId, "reply-1", root.Id.Value);
        await CreateCommentAsync(MockMainAuthorId, "reply-2", root.Id.Value);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        response.EnsureSuccessStatusCode();
        var envelope = await response.Content
            .ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CommentDto returnedRoot = envelope!.Result!.Items.Single();

        Assert.NotNull(returnedRoot.AuthorName);
        Assert.NotNull(returnedRoot.AuthorUsername);
        Assert.StartsWith("DisplayName-", returnedRoot.AuthorName, StringComparison.Ordinal);

        Assert.NotNull(returnedRoot.PreviewChildren);
        Assert.Equal(2, returnedRoot.PreviewChildren!.Count);
        Assert.All(returnedRoot.PreviewChildren, c =>
        {
            Assert.NotNull(c.AuthorName);
            Assert.NotNull(c.AuthorUsername);
            Assert.StartsWith("DisplayName-", c.AuthorName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GetRoots_ShouldReturnPaginatedComments()
    {
        await CreateCommentAsync(MockMainAuthorId, "First comment");
        await CreateCommentAsync(MockMainAuthorId, "Second comment");
        await CreateCommentAsync(MockMainAuthorId, "Third comment");

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetRoots_LastPage_ShouldReturnNullCursor()
    {
        await CreateCommentAsync(MockMainAuthorId, "Comment A");
        await CreateCommentAsync(MockMainAuthorId, "Comment B");

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Equal(2, result.Items.Count);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetRoots_WithLowercaseTargetType_ShouldReturnComments()
    {
        await CreateCommentAsync(MockMainAuthorId, "lowercase target type");

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType=material&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetRoots_EmptyTarget_ShouldReturnEmptyList()
    {
        Guid isolatedEntityId = Guid.NewGuid();

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={isolatedEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task GetChildren_ShouldReturnDirectChildrenOnly()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root comment");

        var childRequest = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Child comment",
            root.Id.Value);
        var childResponse = await AppHttpClient.PostAsJsonAsync("/comments", childRequest);
        childResponse.EnsureSuccessStatusCode();

        var childEnvelope = await childResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Guid childId = childEnvelope!.Result;

        var grandchildRequest = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Grandchild comment",
            childId);
        var grandchildResponse = await AppHttpClient.PostAsJsonAsync("/comments", grandchildRequest);
        grandchildResponse.EnsureSuccessStatusCode();

        var response = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Single(result.Items);
        Assert.Equal(childId, result.Items[0].Id);
    }

    [Fact]
    public async Task GetChildren_WithLowercaseTargetType_ShouldReturnDirectChildren()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root comment");

        var childRequest = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Child comment",
            root.Id.Value);
        var childResponse = await AppHttpClient.PostAsJsonAsync("/comments", childRequest);
        childResponse.EnsureSuccessStatusCode();

        var childEnvelope = await childResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Guid childId = childEnvelope!.Result;

        var response = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType=material&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Single(result.Items);
        Assert.Equal(childId, result.Items[0].Id);
    }

    [Fact]
    public async Task GetChildren_PaginationReturnsCursor_WhenMultipleChildren()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root for pagination");

        for (int i = 1; i <= 3; i++)
        {
            var childRequest = new CreateCommentRequest(
                new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
                $"Child {i}",
                root.Id.Value);
            var childResponse = await AppHttpClient.PostAsJsonAsync("/comments", childRequest);
            childResponse.EnsureSuccessStatusCode();
        }

        var response = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Equal(2, result.Items.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task GetRoots_AndChildren_ShouldKeepDeletedParentInTree()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root comment");
        Comment child = await CreateCommentAsync(MockMainAuthorId, "Child comment", root.Id.Value);

        await ExecuteInDb(async dbContext =>
        {
            Comment entity = await dbContext.Comments.FirstAsync(x => x.Id == root.Id);
            entity.SoftDelete();
            await dbContext.SaveChangesAsync();
        });

        var rootsResponse = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, rootsResponse.StatusCode);

        var rootsEnvelope = await rootsResponse.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(rootsEnvelope);
        Assert.False(rootsEnvelope.IsError);

        CursorResponse<CommentDto> rootsResult = rootsEnvelope.Result!;
        Assert.Single(rootsResult.Items);
        // TotalCount counts only non-deleted root-level comments (depth = 0 AND is_deleted = false).
        // The soft-deleted root still appears in Items as a tombstone (for tree coherence), but
        // is excluded from the count — so the UI badge shows "0 comments" rather than "1".
        Assert.Equal(0, rootsResult.TotalCount);
        Assert.True(rootsResult.Items[0].IsDeleted);
        Assert.True(rootsResult.Items[0].HasMoreChildren);

        var childrenResponse = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, childrenResponse.StatusCode);

        var childrenEnvelope = await childrenResponse.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(childrenEnvelope);
        Assert.False(childrenEnvelope.IsError);

        CursorResponse<CommentDto> childrenResult = childrenEnvelope.Result!;
        Assert.Single(childrenResult.Items);
        Assert.Equal(child.Id.Value, childrenResult.Items[0].Id);
    }

    [Fact]
    public async Task GetRoots_CursorContinuity_SecondPage_ReturnsRemainingItemsWithoutDuplicates()
    {
        // Arrange: create 5 root comments, page size 2 — expect 3 pages total.
        const int totalCount = 5;
        const int pageSize = 2;

        var createdContents = new List<string>();
        for (int i = 1; i <= totalCount; i++)
        {
            string content = $"cursor-continuity-{i}";
            await CreateCommentAsync(MockMainAuthorId, content);
            createdContents.Add(content);
        }

        // Page 1
        var page1Response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit={pageSize}");
        page1Response.EnsureSuccessStatusCode();
        var page1Envelope = await page1Response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> page1 = page1Envelope!.Result!;

        Assert.Equal(pageSize, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);
        Assert.Equal(totalCount, page1.TotalCount);

        // Page 2 — use cursor from page 1
        string encodedCursor1 = Uri.EscapeDataString(page1.NextCursor!);
        var page2Response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit={pageSize}&Cursor={encodedCursor1}");
        page2Response.EnsureSuccessStatusCode();
        var page2Envelope = await page2Response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> page2 = page2Envelope!.Result!;

        Assert.Equal(pageSize, page2.Items.Count);
        Assert.NotNull(page2.NextCursor);

        // Page 3 — remainder (1 item), no NextCursor.
        string encodedCursor2 = Uri.EscapeDataString(page2.NextCursor!);
        var page3Response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit={pageSize}&Cursor={encodedCursor2}");
        page3Response.EnsureSuccessStatusCode();
        var page3Envelope = await page3Response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> page3 = page3Envelope!.Result!;

        Assert.Single(page3.Items);
        Assert.Null(page3.NextCursor);

        // Union of pages must equal all created comments, in creation order, without duplicates.
        var union = page1.Items
            .Concat(page2.Items)
            .Concat(page3.Items)
            .ToList();

        Assert.Equal(totalCount, union.Count);
        Assert.Equal(totalCount, union.Select(x => x.Id).Distinct().Count());
        Assert.Equal(createdContents, union.Select(x => x.Content).ToList());
    }

    [Fact]
    public async Task GetChildren_CursorContinuity_SecondPage_ReturnsRemainingItemsWithoutDuplicates()
    {
        // Arrange: 5 children under a single root, page size 2.
        const int totalChildren = 5;
        const int pageSize = 2;

        Comment root = await CreateCommentAsync(MockMainAuthorId, "cursor-root");
        var createdContents = new List<string>();
        for (int i = 1; i <= totalChildren; i++)
        {
            string content = $"cursor-child-{i}";
            await CreateCommentAsync(MockMainAuthorId, content, root.Id.Value);
            createdContents.Add(content);
        }

        // Page 1
        var page1Response = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit={pageSize}");
        page1Response.EnsureSuccessStatusCode();
        var page1Envelope = await page1Response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> page1 = page1Envelope!.Result!;

        Assert.Equal(pageSize, page1.Items.Count);
        Assert.NotNull(page1.NextCursor);

        // Page 2
        string encodedCursor1 = Uri.EscapeDataString(page1.NextCursor!);
        var page2Response = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit={pageSize}&Cursor={encodedCursor1}");
        page2Response.EnsureSuccessStatusCode();
        var page2Envelope = await page2Response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> page2 = page2Envelope!.Result!;

        Assert.Equal(pageSize, page2.Items.Count);
        Assert.NotNull(page2.NextCursor);

        // Page 3 — remainder
        string encodedCursor2 = Uri.EscapeDataString(page2.NextCursor!);
        var page3Response = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit={pageSize}&Cursor={encodedCursor2}");
        page3Response.EnsureSuccessStatusCode();
        var page3Envelope = await page3Response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> page3 = page3Envelope!.Result!;

        Assert.Single(page3.Items);
        Assert.Null(page3.NextCursor);

        var union = page1.Items
            .Concat(page2.Items)
            .Concat(page3.Items)
            .ToList();

        Assert.Equal(totalChildren, union.Count);
        Assert.Equal(totalChildren, union.Select(x => x.Id).Distinct().Count());
        Assert.Equal(createdContents, union.Select(x => x.Content).ToList());
    }

    [Fact]
    public async Task GetRoots_ShouldReturnPreviewChildrenAndChildrenCount_WhenRootHasReplies()
    {
        // Arrange: 1 root + 5 replies — preview limit on backend is 3, so expect first 3 inline + cursor.
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root with many replies");

        var replyIds = new List<Guid>();
        for (int i = 1; i <= 5; i++)
        {
            Comment reply = await CreateCommentAsync(MockMainAuthorId, $"reply-{i}", root.Id.Value);
            replyIds.Add(reply.Id.Value);
        }

        // Act
        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> result = envelope!.Result!;

        Assert.Single(result.Items);
        CommentDto returnedRoot = result.Items[0];

        Assert.Equal(5, returnedRoot.ChildrenCount);
        Assert.True(returnedRoot.HasMoreChildren);
        Assert.NotNull(returnedRoot.PreviewChildren);
        Assert.Equal(3, returnedRoot.PreviewChildren!.Count);
        Assert.Equal(replyIds.Take(3), returnedRoot.PreviewChildren.Select(c => c.Id));
        // ParentId is denormalized to root.Id in preview rows so frontend can render thread structure.
        Assert.All(returnedRoot.PreviewChildren, c => Assert.Equal(returnedRoot.Id, c.ParentId));
        // Cursor must be present so frontend can fetch the remaining 2 via GET /comments/{id}.
        Assert.NotNull(returnedRoot.PreviewChildrenNextCursor);
    }

    [Fact]
    public async Task GetRoots_ShouldNotEmitPreviewCursor_WhenAllChildrenFitInPreview()
    {
        // 1 root + 2 replies (under preview cap of 3) — cursor must be null.
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root with few replies");
        await CreateCommentAsync(MockMainAuthorId, "reply-1", root.Id.Value);
        await CreateCommentAsync(MockMainAuthorId, "reply-2", root.Id.Value);

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CommentDto returnedRoot = envelope!.Result!.Items[0];

        Assert.Equal(2, returnedRoot.ChildrenCount);
        Assert.NotNull(returnedRoot.PreviewChildren);
        Assert.Equal(2, returnedRoot.PreviewChildren!.Count);
        Assert.Null(returnedRoot.PreviewChildrenNextCursor);
    }

    [Fact]
    public async Task GetRoots_PreviewCursor_ShouldFeedSubsequentChildrenPage()
    {
        // 1 root + 5 replies — fetch preview, then use its cursor on /comments/{id}
        // and assert no overlap + remaining items returned.
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root for cursor handoff");

        var replyIds = new List<Guid>();
        for (int i = 1; i <= 5; i++)
        {
            Comment reply = await CreateCommentAsync(MockMainAuthorId, $"reply-{i}", root.Id.Value);
            replyIds.Add(reply.Id.Value);
        }

        var rootsResp = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");
        var rootsEnv = await rootsResp.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CommentDto returnedRoot = rootsEnv!.Result!.Items[0];

        string cursor = returnedRoot.PreviewChildrenNextCursor!;

        var nextResp = await AppHttpClient.GetAsync(
            $"/comments/{returnedRoot.Id}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}" +
            $"&Limit=10&Cursor={Uri.EscapeDataString(cursor)}");

        Assert.Equal(HttpStatusCode.OK, nextResp.StatusCode);
        var nextEnv = await nextResp.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CursorResponse<CommentDto> next = nextEnv!.Result!;

        // Preview already contained replies 1-3, so next page must be 4-5 with no overlap.
        Assert.Equal(2, next.Items.Count);
        Assert.Equal(replyIds.Skip(3), next.Items.Select(c => c.Id));
        Assert.Null(next.NextCursor);
    }

    [Fact]
    public async Task GetChildren_ShouldNotEmitPreviewChildren()
    {
        // PreviewChildren is a root-listing-only optimization. /comments/{id} must keep
        // returning a flat CursorResponse with PreviewChildren = null on every item.
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root");
        await CreateCommentAsync(MockMainAuthorId, "child", root.Id.Value);

        var response = await AppHttpClient.GetAsync(
            $"/comments/{root.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        CommentDto child = envelope!.Result!.Items.Single();

        Assert.Null(child.PreviewChildren);
        Assert.Null(child.PreviewChildrenNextCursor);
        Assert.Equal(0, child.ChildrenCount);
    }

    [Fact]
    public async Task CreateComment_WithLowercaseTargetType_ShouldCreateComment()
    {
        var request = new
        {
            EntityReference = new
            {
                Type = "material",
                Id = TestTargetEntityId,
            },
            Content = "lowercase json target type",
            ParentId = (Guid?)null,
        };

        var response = await AppHttpClient.PostAsJsonAsync("/comments", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotEqual(Guid.Empty, envelope.Result);
    }

    [Fact]
    public async Task GetThread_ShouldReturnAllDescendantsAfterAnchor()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root comment");
        Comment child = await CreateCommentAsync(MockMainAuthorId, "Child comment", root.Id.Value);
        Comment anchor = await CreateCommentAsync(MockMainAuthorId, "Anchor comment", child.Id.Value);
        Comment threadReply = await CreateCommentAsync(MockMainAuthorId, "Thread reply", anchor.Id.Value);
        Comment nestedThreadReply = await CreateCommentAsync(MockMainAuthorId, "Nested thread reply", threadReply.Id.Value);

        var response = await AppHttpClient.GetAsync(
            $"/comments/{anchor.Id.Value}/thread?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Equal(2, result.Items.Count);
        Assert.DoesNotContain(result.Items, comment => comment.Id == anchor.Id.Value);
        Assert.Contains(result.Items, comment => comment.Id == threadReply.Id.Value);
        Assert.Contains(result.Items, comment => comment.Id == nestedThreadReply.Id.Value);

        CommentDto directThreadReply = Assert.Single(result.Items, comment => comment.Id == threadReply.Id.Value);
        Assert.Equal(anchor.Id.Value, directThreadReply.ParentId);
        Assert.Equal("Anchor comment", directThreadReply.ParentPreview);

        CommentDto nestedThreadItem = Assert.Single(result.Items, comment => comment.Id == nestedThreadReply.Id.Value);
        Assert.Equal(threadReply.Id.Value, nestedThreadItem.ParentId);
        Assert.Equal("Thread reply", nestedThreadItem.ParentPreview);
    }

    [Fact]
    public async Task GetThread_ShouldNotReturnAnchorOrSiblingBranches()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root comment");
        Comment child = await CreateCommentAsync(MockMainAuthorId, "Child comment", root.Id.Value);
        Comment anchor = await CreateCommentAsync(MockMainAuthorId, "Anchor comment", child.Id.Value);
        Comment threadReply = await CreateCommentAsync(MockMainAuthorId, "Thread reply", anchor.Id.Value);
        Comment sibling = await CreateCommentAsync(MockMainAuthorId, "Sibling comment", child.Id.Value);
        Comment siblingReply = await CreateCommentAsync(MockMainAuthorId, "Sibling reply", sibling.Id.Value);

        var response = await AppHttpClient.GetAsync(
            $"/comments/{anchor.Id.Value}/thread?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CursorResponse<CommentDto> result = envelope.Result!;
        Assert.Single(result.Items);
        Assert.Equal(threadReply.Id.Value, result.Items[0].Id);
        Assert.DoesNotContain(result.Items, comment => comment.Id == anchor.Id.Value);
        Assert.DoesNotContain(result.Items, comment => comment.Id == sibling.Id.Value);
        Assert.DoesNotContain(result.Items, comment => comment.Id == siblingReply.Id.Value);
    }

    [Fact]
    public async Task GetThread_ShouldPaginateWithCursor()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "Root comment");
        Comment child = await CreateCommentAsync(MockMainAuthorId, "Child comment", root.Id.Value);
        Comment anchor = await CreateCommentAsync(MockMainAuthorId, "Anchor comment", child.Id.Value);
        await CreateCommentAsync(MockMainAuthorId, "Thread reply 1", anchor.Id.Value);
        await CreateCommentAsync(MockMainAuthorId, "Thread reply 2", anchor.Id.Value);
        await CreateCommentAsync(MockMainAuthorId, "Thread reply 3", anchor.Id.Value);

        var firstResponse = await AppHttpClient.GetAsync(
            $"/comments/{anchor.Id.Value}/thread?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=2");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstEnvelope = await firstResponse.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(firstEnvelope);

        CursorResponse<CommentDto> firstResult = firstEnvelope.Result!;
        Assert.Equal(2, firstResult.Items.Count);
        Assert.NotNull(firstResult.NextCursor);

        string cursor = WebUtility.UrlEncode(firstResult.NextCursor);
        var secondResponse = await AppHttpClient.GetAsync(
            $"/comments/{anchor.Id.Value}/thread?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=2&Cursor={cursor}");

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondEnvelope = await secondResponse.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(secondEnvelope);

        CursorResponse<CommentDto> secondResult = secondEnvelope.Result!;
        Assert.Single(secondResult.Items);
        Assert.Null(secondResult.NextCursor);
    }
}
