using System.Net;
using System.Net.Http.Json;
using Common;
using CommentService.Contracts.Comments.Dtos;
using CommentService.Domain;
using CommentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetCommentAncestorsTests : CommentServiceTestsBase
{
    public GetCommentAncestorsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAncestors_RootComment_ReturnsEmptyChain()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "root");

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/{root.Id.Value}/ancestors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content
            .ReadFromJsonAsync<Envelope<CommentAncestorsDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        CommentAncestorsDto dto = envelope.Result!;
        Assert.Equal(0, dto.Depth);
        Assert.Equal(TestTargetEntityType.ToString().ToLowerInvariant(), dto.TargetType);
        Assert.Equal(TestTargetEntityId, dto.TargetId);
        Assert.Empty(dto.AncestorIds);
    }

    [Fact]
    public async Task GetAncestors_DeepReply_ReturnsChainFromRootToParent()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "root");
        Comment d1 = await CreateCommentAsync(MockSecondAuthorId, "d1", root.Id.Value);
        Comment d2 = await CreateCommentAsync(MockMainAuthorId, "d2", d1.Id.Value);
        Comment d3 = await CreateCommentAsync(MockSecondAuthorId, "d3", d2.Id.Value);

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/{d3.Id.Value}/ancestors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var envelope = await response.Content
            .ReadFromJsonAsync<Envelope<CommentAncestorsDto>>();
        CommentAncestorsDto dto = envelope!.Result!;

        Assert.Equal(3, dto.Depth);
        Assert.Equal(
            new[] { root.Id.Value, d1.Id.Value, d2.Id.Value },
            dto.AncestorIds);
    }

    [Fact]
    public async Task GetAncestors_NonExistent_Returns404()
    {
        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/{Guid.NewGuid()}/ancestors");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAncestors_SoftDeletedComment_Returns404()
    {
        // SQL filter `is_deleted = FALSE` отдельная code-path от truly missing —
        // tombstones для soft-delete'нутых child'ов не должны утекать ancestor chain.
        Comment root = await CreateCommentAsync(MockMainAuthorId, "root");
        Comment reply = await CreateCommentAsync(MockSecondAuthorId, "to delete", root.Id.Value);

        // Mutate is_deleted=true прямо через DbContext, минуя soft-delete API,
        // чтобы изолировать предикат в endpoint'е.
        await ExecuteInDb(async db =>
        {
            Comment? loaded = await db.Comments
                .IgnoreQueryFilters()
                .FirstAsync(c => c.Id == reply.Id);
            loaded!.SoftDelete();
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/{reply.Id.Value}/ancestors");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAncestors_AccessDenied_Returns403()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "root");

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        EntitlementChecker.DenyAll();

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/{root.Id.Value}/ancestors");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAncestors_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/{Guid.NewGuid()}/ancestors");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAncestors_AdminBypassesEntitlementCheck()
    {
        Comment root = await CreateCommentAsync(MockMainAuthorId, "root");

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        EntitlementChecker.DenyAll();

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/{root.Id.Value}/ancestors");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
