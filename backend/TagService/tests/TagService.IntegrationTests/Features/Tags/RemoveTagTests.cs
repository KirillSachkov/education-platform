using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TagService.Contracts.Tags.Requests;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RemoveTagTests : TagServiceTestsBase
{
    public RemoveTagTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RemoveTags_WithExistingLinks_ShouldDeleteOnlySpecifiedLinks()
    {
        var firstTag = await CreateTagAsync("first");
        var secondTag = await CreateTagAsync("second");

        await CreateLinkAsync(firstTag.Id.Value);
        await CreateLinkAsync(secondTag.Id.Value);
        await CreateLinkAsync(firstTag.Id.Value, entityId: SecondEntityId);

        var request = new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [firstTag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(TestEntityId, envelope.Result);

        int removedLinks = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == firstTag.Id));
        Assert.Equal(0, removedLinks);

        int remainingLinksOnEntity = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == secondTag.Id));
        Assert.Equal(1, remainingLinksOnEntity);

        int otherEntityLinks = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == SecondEntityId &&
            x.TagId == firstTag.Id));
        Assert.Equal(1, otherEntityLinks);
    }

    [Fact]
    public async Task RemoveTags_WithEmptyTagIds_ShouldReturnBadRequest()
    {
        var request = new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = []
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveTags_WithDuplicateTagIds_ShouldReturnBadRequest()
    {
        var tag = await CreateTagAsync("duplicate");
        var request = new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value, tag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveTags_WithEmptyEntityType_ShouldReturnBadRequest()
    {
        var tag = await CreateTagAsync("entity-type-empty");

        var request = new RemoveTagRequest
        {
            EntityType = string.Empty,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveTags_WithEmptyEntityId_ShouldReturnBadRequest()
    {
        var tag = await CreateTagAsync("entity-id-empty");

        var request = new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = Guid.Empty,
            TagIds = [tag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
