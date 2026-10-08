using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class AddTagsTests : TagServiceTestsBase
{
    public AddTagsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AddTags_WithCanonTagIdOnly_ShouldReturnOk_AndCreateLink()
    {
        Tag tag = await CreateTagAsync("csharp");
        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value],
            TagTitles = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(TestEntityId, envelope.Result);

        int linksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == tag.Id));
        Assert.Equal(1, linksCount);
    }

    [Fact]
    public async Task AddTags_WithExistingTagTitle_ShouldCreateLink()
    {
        Tag tag = await CreateTagAsync("algebra");

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = SecondEntityId,
            TagIds = [],
            TagTitles = [tag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int linksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == SecondEntityId &&
            x.TagId == tag.Id));

        Assert.Equal(1, linksCount);
    }

    [Fact]
    public async Task AddTags_WithNewTagTitle_ShouldCreateTagAndLink()
    {
        const string newTitle = "new-dotnet-tag";

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = SecondEntityId,
            TagIds = [],
            TagTitles = [newTitle]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Tag? createdTag = await ExecuteInDb(async db =>
            (await db.Tags.ToListAsync()).FirstOrDefault(x => x.Title.Value == newTitle));

        Assert.NotNull(createdTag);

        int linksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == SecondEntityId &&
            x.TagId == createdTag!.Id));

        Assert.Equal(1, linksCount);
    }

    [Fact]
    public async Task AddTags_WithAliasTagId_ShouldReturnOk_AndCreateCanonicalLink()
    {
        Tag canonicalTag = await CreateTagAsync("python");
        Tag aliasTag = await CreateTagAsync("py");

        await ExecuteInDb(async db =>
        {
            Tag dbAliasTag = await db.Tags.FirstAsync(x => x.Id == aliasTag.Id);
            dbAliasTag.MarkAsAlias();
            await db.SaveChangesAsync();
        });

        await CreateAliasAsync(canonicalTag.Id.Value, aliasTag.Id.Value);

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [aliasTag.Id.Value],
            TagTitles = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int canonLinksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == canonicalTag.Id));
        Assert.Equal(1, canonLinksCount);

        int aliasLinksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == aliasTag.Id));
        Assert.Equal(0, aliasLinksCount);
    }

    [Fact]
    public async Task AddTags_WithTagIdsAndTagTitles_ShouldCreateBothLinks()
    {
        Tag idOnlyTag = await CreateTagAsync("nodejs");
        Tag titleTag = await CreateTagAsync("typescript");

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [idOnlyTag.Id.Value],
            TagTitles = [titleTag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int idOnlyLinksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == idOnlyTag.Id));
        Assert.Equal(1, idOnlyLinksCount);

        int titleLinksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == titleTag.Id));
        Assert.Equal(1, titleLinksCount);
    }

    [Fact]
    public async Task AddTags_WithTagIdsAndTagTitles_WhenTagIdsContainAlias_ShouldCreateCanonicalAndTitleLinks()
    {
        Tag canonicalTag = await CreateTagAsync("golang");
        Tag aliasTag = await CreateTagAsync("go");
        Tag titleTag = await CreateTagAsync("backend");

        await ExecuteInDb(async db =>
        {
            Tag dbAliasTag = await db.Tags.FirstAsync(x => x.Id == aliasTag.Id);
            dbAliasTag.MarkAsAlias();
            await db.SaveChangesAsync();
        });

        await CreateAliasAsync(canonicalTag.Id.Value, aliasTag.Id.Value);

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [aliasTag.Id.Value],
            TagTitles = [titleTag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int canonicalLinksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == canonicalTag.Id));
        Assert.Equal(1, canonicalLinksCount);

        int titleLinksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == titleTag.Id));
        Assert.Equal(1, titleLinksCount);
    }

    [Fact]
    public async Task AddTags_WithTagIdsAndTagTitles_DuplicateRawValues_ShouldReturnBadRequest()
    {
        Guid id = Guid.NewGuid();

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [id],
            TagTitles = [id.ToString()]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_WithDuplicateTagIds_ShouldReturnBadRequest()
    {
        Tag tag = await CreateTagAsync("ai");

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value, tag.Id.Value],
            TagTitles = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_WithEmptyEntityType_ShouldReturnBadRequest()
    {
        Tag tag = await CreateTagAsync("backend-api");

        var request = new AddTagsRequest
        {
            EntityType = string.Empty,
            EntityId = TestEntityId,
            TagIds = [tag.Id.Value],
            TagTitles = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_WithEmptyEntityId_ShouldReturnBadRequest()
    {
        Tag tag = await CreateTagAsync("backend-dev");

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = Guid.Empty,
            TagIds = [tag.Id.Value],
            TagTitles = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_WithEmptyTagIdsAndTitles_ShouldReturnBadRequest()
    {
        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_WithTooManyTags_ShouldReturnBadRequest()
    {
        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = Enumerable.Range(1, 101).Select(index => $"tag-{index}").ToArray()
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddTags_WithNonExistentTagId_ShouldReturnNotFound()
    {
        Guid nonExistentTagId = Guid.NewGuid();

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [nonExistentTagId],
            TagTitles = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        int linksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == TagId.Of(nonExistentTagId)));
        Assert.Equal(0, linksCount);
    }

    [Fact]
    public async Task AddTags_WhenLinkAlreadyExistsByTitle_ShouldReturnConflict()
    {
        Tag tag = await CreateTagAsync("python");
        await CreateLinkAsync(tag.Id.Value);

        var request = new AddTagsRequest
        {
            EntityType = TestEntityType,
            EntityId = TestEntityId,
            TagIds = [],
            TagTitles = [tag.Title.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags/entity", request);

        await AssertConflictResponseAsync(
            response,
            "tag.entity.already.exists",
            "Этот тег уже привязан к сущности");

        int linksCount = await ExecuteInDb(db => db.EntityTags.CountAsync(x =>
            x.EntityReference.Type == TestEntityTypeValue &&
            x.EntityReference.Id == TestEntityId &&
            x.TagId == tag.Id));
        Assert.Equal(1, linksCount);
    }
}
