using System.Net;
using System.Net.Http.Json;
using CommentService.Core.Features.Admin;
using CommentService.IntegrationTests.Infrastructure;
using Common;
using SharedKernel;

namespace CommentService.IntegrationTests.Features.Admin;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetUserCommentsForAdminTests : CommentServiceTestsBase
{
    public GetUserCommentsForAdminTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // Регрессия: раньше raw SQL в GetUserCommentsForAdminHandler ссылался на несуществующие
    // колонки (entity_type/entity_id/user_id/body/deleted_at/parent_id) → каждый вызов падал
    // с Postgres-ошибкой 500. После выравнивания под фактическую схему (author_id,
    // target_entity_type, target_entity_id, content, deletion_date) endpoint обязан вернуть 200
    // с комментариями автора.
    [Fact]
    public async Task GetRecent_WhenUserAuthoredComment_Returns200WithComment()
    {
        // Arrange — комментарий, написанный целевым автором.
        const string content = "комментарий целевого автора для admin-выборки";
        await CreateCommentAsync(
            authorId: MockSecondAuthorId,
            content: content,
            parentId: null);

        // Дефолтная аутентификация в базовом классе — platform-admin (включает Users.VIEW).

        // Act
        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/comments/admin/users/{MockSecondAuthorId}/recent");

        // Assert — НЕ 500 (баг), а 200 с непустым списком и совпадающим preview.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<AdminUserCommentsResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<AdminUserCommentsResponse>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.NotEmpty(envelope.Result.Items);

        AdminUserCommentRow row = Assert.Single(envelope.Result.Items);
        Assert.Equal(content, row.BodyPreview);
        // target_entity_type хранится в нижнем регистре (HasConversion → ToLowerInvariant).
        Assert.Equal(TestTargetEntityType.ToString().ToLowerInvariant(), row.EntityType);
        Assert.Equal(TestTargetEntityId, row.EntityId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task GetRecent_WhenLimitIsOutOfRange_Returns400(int limit)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/comments/admin/users/{MockSecondAuthorId}/recent?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRecent_WithoutAuthentication_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/comments/admin/users/{MockSecondAuthorId}/recent");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRecent_WithoutUsersViewPermission_Returns403()
    {
        AuthenticateAs(MockMainAuthorId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/comments/admin/users/{MockSecondAuthorId}/recent");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
