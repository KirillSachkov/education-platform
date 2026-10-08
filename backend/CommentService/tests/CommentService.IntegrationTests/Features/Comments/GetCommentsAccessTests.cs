using System.Net;
using System.Net.Http.Json;
using Common;
using CommentService.Contracts;
using CommentService.Contracts.Comments.Dtos;
using CommentService.Contracts.Comments.Requests;
using CommentService.Domain;
using CommentService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetCommentsAccessTests : CommentServiceTestsBase
{
    public GetCommentsAccessTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GetRoots_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetChildren_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        Guid parentId = Guid.NewGuid();

        var response = await AppHttpClient.GetAsync(
            $"/comments/{parentId}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetThread_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        Guid parentId = Guid.NewGuid();

        var response = await AppHttpClient.GetAsync(
            $"/comments/{parentId}/thread?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRoots_WhenAccessDenied_ReturnsForbidden()
    {
        await CreateCommentAsync(MockMainAuthorId, "visible comment");

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        EntitlementChecker.DenyAll();

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRoots_WhenAccessGranted_ReturnsComments()
    {
        await CreateCommentAsync(MockMainAuthorId, "visible comment");

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        EntitlementChecker.GrantAll();

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotEmpty(envelope.Result!.Items);
    }

    [Fact]
    public async Task GetChildren_WhenAccessDenied_ReturnsForbidden()
    {
        Comment parent = await CreateCommentAsync(MockMainAuthorId, "parent");
        var childRequest = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "child",
            parent.Id.Value);
        await AppHttpClient.PostAsJsonAsync("/comments", childRequest);

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        EntitlementChecker.DenyAll();

        var response = await AppHttpClient.GetAsync(
            $"/comments/{parent.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetThread_WhenAccessDenied_ReturnsForbidden()
    {
        Comment parent = await CreateCommentAsync(MockMainAuthorId, "parent");
        await CreateCommentAsync(MockMainAuthorId, "child", parent.Id.Value);

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        EntitlementChecker.DenyAll();

        var response = await AppHttpClient.GetAsync(
            $"/comments/{parent.Id.Value}/thread?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetRoots_AdminBypassesEntitlementCheck()
    {
        await CreateCommentAsync(MockMainAuthorId, "admin visible");

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        EntitlementChecker.DenyAll();

        var response = await AppHttpClient.GetAsync(
            $"/comments?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotEmpty(envelope.Result!.Items);
    }

    [Fact]
    public async Task GetChildren_AdminBypassesEntitlementCheck()
    {
        Comment parent = await CreateCommentAsync(MockMainAuthorId, "parent");
        await CreateCommentAsync(MockMainAuthorId, "child", parent.Id.Value);

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        EntitlementChecker.DenyAll();

        var response = await AppHttpClient.GetAsync(
            $"/comments/{parent.Id.Value}?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotEmpty(envelope.Result!.Items);
    }

    [Fact]
    public async Task GetThread_AdminBypassesEntitlementCheck()
    {
        Comment parent = await CreateCommentAsync(MockMainAuthorId, "parent");
        await CreateCommentAsync(MockMainAuthorId, "child", parent.Id.Value);

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/comments/{parent.Id.Value}/thread?TargetType={TestTargetEntityType}&TargetId={TestTargetEntityId}&Limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<CursorResponse<CommentDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CommentDto>>>();
        Assert.NotNull(envelope);
        Assert.NotEmpty(envelope.Result!.Items);
    }
}
