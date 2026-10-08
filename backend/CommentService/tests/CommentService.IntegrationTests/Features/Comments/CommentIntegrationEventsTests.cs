using System.Net;
using System.Net.Http.Json;
using CommentService.Contracts.Comments.Requests;
using CommentService.IntegrationTests.Infrastructure;
using Common;
using Shared.Messaging.IntegrationEvents.Comments.Events;

namespace CommentService.IntegrationTests.Features.Comments;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CommentIntegrationEventsTests : CommentServiceTestsBase
{
    public CommentIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateComment_ShouldPublishCommentCreated()
    {
        // L2-тест: ловит «забыл flush'нуть outbox» — handler публикует `CommentCreated`
        // через IOutboxService. В тестах IOutboxService подменён на TestOutboxService,
        // который пишет в OutboxCollector — Wolverine envelope-storage не задействован.
        var request = new CreateCommentRequest(
            new EntityReferenceDto(TestTargetEntityType, TestTargetEntityId),
            "Тестовый коммент для outbox-flush проверки",
            null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/comments", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CommentCreated published = OutboxCollector.OfType<CommentCreated>().Single();
        Assert.Equal(MockMainAuthorId, published.AuthorId);
        Assert.Equal(TestTargetEntityId, published.EntityId);
        Assert.Equal("material", published.EntityType);
        Assert.Null(published.ParentId);
        Assert.Contains("Тестовый коммент", published.Preview, StringComparison.Ordinal);
    }
}
