using System.Net;
using System.Net.Http.Json;
using Shared.Messaging.IntegrationEvents.Tags.Events;
using TagService.Contracts.Tags.Requests;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class TagIntegrationEventsTests : TagServiceTestsBase
{
    public TagIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AddTags_WithResolvedTags_ShouldPublishTagsAddedToEntity()
    {
        var tag = await CreateTagAsync("algebra");

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = [tag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TagsAddedToEntity published = Factory.OutboxCollector.OfType<TagsAddedToEntity>().Single();
        Assert.Equal(TestEntityId, published.EntityId);
        Assert.Equal(TestEntityTypeValue, published.EntityType);
        Assert.Equal([tag.Id.Value], published.TagIds);
    }

    [Fact]
    public async Task RemoveTags_ShouldPublishTagsRemovedFromEntity()
    {
        var tag = await CreateTagAsync("backend");
        await CreateLinkAsync(tag.Id.Value);

        var request = new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TagsRemovedFromEntity published = Factory.OutboxCollector.OfType<TagsRemovedFromEntity>().Single();
        Assert.Equal(TestEntityId, published.EntityId);
        Assert.Equal(TestEntityTypeValue, published.EntityType);
        Assert.Equal([tag.Id.Value], published.TagIds);
    }

    [Fact]
    public async Task RemoveTags_WithMissingRequestedId_ShouldPublishOnlyRemovedTags()
    {
        var tag = await CreateTagAsync("remove-existing-only");
        await CreateLinkAsync(tag.Id.Value);

        HttpResponseMessage response = await DeleteAsJsonAsync("/tags/entity", new RemoveTagRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value, Guid.CreateVersion7()]
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        TagsRemovedFromEntity published = Factory.OutboxCollector.OfType<TagsRemovedFromEntity>().Single();
        Assert.Equal([tag.Id.Value], published.TagIds);
    }

    [Fact]
    public async Task DeleteTag_ShouldPublishTagsDeleted()
    {
        var tag = await CreateTagAsync("csharp");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/tags/{tag.Id.Value}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TagsDeleted published = Factory.OutboxCollector.OfType<TagsDeleted>().Single();
        Assert.Equal([tag.Id.Value], published.TagIds);
    }

    [Fact]
    public async Task UpdateTag_ShouldPublishTagsUpdated()
    {
        var tag = await CreateTagAsync("old-title");
        var request = new UpdateTagRequest { Title = "New Title" };

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/tags/{tag.Id.Value}", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TagUpdated published = Factory.OutboxCollector.OfType<TagUpdated>().Single();
        Assert.Equal(tag.Id.Value, published.TagId);
    }

    [Fact]
    public async Task MergeTags_ShouldPublishTagsMergedForMergedAliasIds()
    {
        var canonicalTag = await CreateTagAsync("dotnet");
        var aliasTag1 = await CreateTagAsync("net");
        var aliasTag2 = await CreateTagAsync("aspnet");

        var request = new MergeTagsRequest
        {
            TagIds = [aliasTag1.Id.Value, aliasTag2.Id.Value]
        };

        HttpResponseMessage response =
            await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TagsMerged published = Factory.OutboxCollector.OfType<TagsMerged>().Single();
        Assert.Equal(canonicalTag.Id.Value, published.CanonicalTagId);
        Assert.Equal(request.TagIds, published.AliasTagIds);
    }
}
