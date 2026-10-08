using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class MergeTagsTests : TagServiceTestsBase
{
    public MergeTagsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task MergeTags_WithValidRequest_ShouldCreateAlias_AndUpdateKinds()
    {
        Tag canonicalTag = await CreateTagAsync("C#");
        Tag aliasTag = await CreateTagAsync("c-sharp");

        var request = new MergeTagsRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(canonicalTag.Id.Value, envelope.Result);

        int aliasesCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.AliasTagId == aliasTag.Id));
        Assert.Equal(1, aliasesCount);

        Tag dbCanonicalTag = await ExecuteInDb(db => db.Tags.FirstAsync(x => x.Id == canonicalTag.Id));
        Tag dbAliasTag = await ExecuteInDb(db => db.Tags.FirstAsync(x => x.Id == aliasTag.Id));
        Assert.Equal(TagKind.CANON, dbCanonicalTag.Kind);
        Assert.Equal(TagKind.ALIAS, dbAliasTag.Kind);
    }

    [Fact]
    public async Task MergeTags_WithEntityLinksByAlias_ShouldReplaceLinksWithCanonicalTag()
    {
        Tag canonicalTag = await CreateTagAsync("java-script");
        Tag aliasTag = await CreateTagAsync("js");
        await CreateLinkAsync(aliasTag.Id.Value);

        var request = new MergeTagsRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int canonicalLinksCount = await ExecuteInDb(db =>
            db.EntityTags.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.EntityReference.Type == TestEntityTypeValue &&
                x.EntityReference.Id == TestEntityId));
        Assert.Equal(1, canonicalLinksCount);

        int aliasLinksCount = await ExecuteInDb(db =>
            db.EntityTags.CountAsync(x =>
                x.TagId == aliasTag.Id &&
                x.EntityReference.Type == TestEntityTypeValue &&
                x.EntityReference.Id == TestEntityId));
        Assert.Equal(0, aliasLinksCount);
    }

    [Fact]
    public async Task MergeTags_WhenEntityAlreadyHasCanonicalAndAliasLinks_ShouldKeepSingleCanonicalLink()
    {
        Tag canonicalTag = await CreateTagAsync("typescript");
        Tag aliasTag = await CreateTagAsync("ts");

        await CreateLinkAsync(canonicalTag.Id.Value);
        await CreateLinkAsync(aliasTag.Id.Value);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/tags/{canonicalTag.Id.Value}/aliases",
            new MergeTagsRequest
            {
                TagIds = [aliasTag.Id.Value]
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int canonicalLinksCount = await ExecuteInDb(db =>
            db.EntityTags.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.EntityReference.Type == TestEntityTypeValue &&
                x.EntityReference.Id == TestEntityId));
        Assert.Equal(1, canonicalLinksCount);

        int aliasLinksCount = await ExecuteInDb(db =>
            db.EntityTags.CountAsync(x =>
                x.TagId == aliasTag.Id &&
                x.EntityReference.Type == TestEntityTypeValue &&
                x.EntityReference.Id == TestEntityId));
        Assert.Equal(0, aliasLinksCount);
    }

    [Fact]
    public async Task MergeTags_WhenEntityHasSeveralMergedAliases_ShouldKeepSingleCanonicalLink()
    {
        Tag canonicalTag = await CreateTagAsync("nodejs");
        Tag firstAlias = await CreateTagAsync("node");
        Tag secondAlias = await CreateTagAsync("node-js");

        await CreateLinkAsync(firstAlias.Id.Value);
        await CreateLinkAsync(secondAlias.Id.Value);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/tags/{canonicalTag.Id.Value}/aliases",
            new MergeTagsRequest
            {
                TagIds = [firstAlias.Id.Value, secondAlias.Id.Value]
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int canonicalLinksCount = await ExecuteInDb(db =>
            db.EntityTags.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.EntityReference.Type == TestEntityTypeValue &&
                x.EntityReference.Id == TestEntityId));
        Assert.Equal(1, canonicalLinksCount);

        int aliasLinksCount = await ExecuteInDb(db =>
            db.EntityTags.CountAsync(x =>
                (x.TagId == firstAlias.Id || x.TagId == secondAlias.Id) &&
                x.EntityReference.Type == TestEntityTypeValue &&
                x.EntityReference.Id == TestEntityId));
        Assert.Equal(0, aliasLinksCount);
    }

    [Fact]
    public async Task MergeTags_WithSelfAlias_ShouldReturnBadRequest()
    {
        Tag canonicalTag = await CreateTagAsync("self");

        var request = new MergeTagsRequest
        {
            TagIds = [canonicalTag.Id.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MergeTags_WithNonExistentAliasTag_ShouldReturnNotFound()
    {
        Tag canonicalTag = await CreateTagAsync("java");

        var request = new MergeTagsRequest
        {
            TagIds = [Guid.NewGuid()]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MergeTags_WithDuplicateAliasTagIds_ShouldReturnBadRequest()
    {
        Tag canonicalTag = await CreateTagAsync("backend");
        Tag aliasTag = await CreateTagAsync("backend-dev");

        var request = new MergeTagsRequest
        {
            TagIds = [aliasTag.Id.Value, aliasTag.Id.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MergeTags_WithEmptyAliasTagIds_ShouldReturnBadRequest()
    {
        Tag canonicalTag = await CreateTagAsync("backend");

        var request = new MergeTagsRequest
        {
            TagIds = []
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MergeTags_WithTooManyAliasTagIds_ShouldReturnBadRequest()
    {
        Tag canonicalTag = await CreateTagAsync("bounded-merge");
        var request = new MergeTagsRequest
        {
            TagIds = Enumerable.Range(0, 101).Select(_ => Guid.CreateVersion7()).ToArray()
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/tags/{canonicalTag.Id.Value}/aliases",
            request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MergeTags_WithEmptyTagId_ShouldReturnBadRequest()
    {
        Tag aliasTag = await CreateTagAsync("dotnet-core");

        var request = new MergeTagsRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{Guid.Empty}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MergeTags_WithNonExistentCanonicalTag_ShouldReturnNotFound()
    {
        Tag aliasTag = await CreateTagAsync("kotlin");

        var request = new MergeTagsRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{Guid.NewGuid()}/aliases", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MergeTags_WhenTargetIsAlias_ShouldReturnBadRequest()
    {
        Tag actualCanonical = await CreateTagAsync("canonical");
        Tag aliasTarget = await CreateTagAsync("alias-target");
        Tag newAlias = await CreateTagAsync("new-alias");

        await ExecuteInDb(async db =>
        {
            Tag dbAliasTarget = await db.Tags.FirstAsync(x => x.Id == aliasTarget.Id);
            dbAliasTarget.MarkAsAlias();
            await db.SaveChangesAsync();
        });
        await CreateAliasAsync(actualCanonical.Id.Value, aliasTarget.Id.Value);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/tags/{aliasTarget.Id.Value}/aliases",
            new MergeTagsRequest { TagIds = [newAlias.Id.Value] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await ExecuteInDb(db => db.TagAliases.CountAsync(x => x.TagId == aliasTarget.Id)));
    }

    [Fact]
    public async Task MergeTags_WithOnlyAlreadyMergedAlias_ShouldReturnConflict()
    {
        Tag canonicalTag = await CreateTagAsync("golang");
        Tag aliasTag = await CreateTagAsync("go");

        await CreateAliasAsync(canonicalTag.Id.Value, aliasTag.Id.Value);

        var request = new MergeTagsRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        await AssertConflictResponseAsync(
            response,
            "tag.alias.already.exists",
            "Этот алиас уже привязан к тегу");

        int aliasesCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.AliasTagId == aliasTag.Id));
        Assert.Equal(1, aliasesCount);
    }

    [Fact]
    public async Task MergeTags_WithExistingAndNewAliases_ShouldReturnConflict_AndNotCreateNewAlias()
    {
        Tag canonicalTag = await CreateTagAsync("python");
        Tag existingAlias = await CreateTagAsync("py");
        Tag newAlias = await CreateTagAsync("python3");

        await CreateAliasAsync(canonicalTag.Id.Value, existingAlias.Id.Value);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/tags/{canonicalTag.Id.Value}/aliases",
            new MergeTagsRequest
            {
                TagIds = [existingAlias.Id.Value, newAlias.Id.Value]
            });

        await AssertConflictResponseAsync(
            response,
            "tag.alias.already.exists",
            "Этот алиас уже привязан к тегу");

        int canonicalAliasesCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x => x.TagId == canonicalTag.Id));
        Assert.Equal(1, canonicalAliasesCount);

        int existingAliasCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.AliasTagId == existingAlias.Id));
        Assert.Equal(1, existingAliasCount);

        int newAliasCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.AliasTagId == newAlias.Id));
        Assert.Equal(0, newAliasCount);
    }

    [Fact]
    public async Task MergeTags_WithOnlyNewAlias_ShouldKeepAlreadyMergedAliases()
    {
        Tag canonicalTag = await CreateTagAsync("dotnet");
        Tag firstAlias = await CreateTagAsync("net");
        Tag secondAlias = await CreateTagAsync("aspnet");

        HttpResponseMessage firstMergeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/tags/{canonicalTag.Id.Value}/aliases",
            new MergeTagsRequest
            {
                TagIds = [firstAlias.Id.Value]
            });
        Assert.Equal(HttpStatusCode.OK, firstMergeResponse.StatusCode);

        HttpResponseMessage secondMergeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/tags/{canonicalTag.Id.Value}/aliases",
            new MergeTagsRequest
            {
                TagIds = [secondAlias.Id.Value]
            });
        Assert.Equal(HttpStatusCode.OK, secondMergeResponse.StatusCode);

        int aliasesCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x => x.TagId == canonicalTag.Id));
        Assert.Equal(2, aliasesCount);

        int firstAliasCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.AliasTagId == firstAlias.Id));
        Assert.Equal(1, firstAliasCount);

        int secondAliasCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x =>
                x.TagId == canonicalTag.Id &&
                x.AliasTagId == secondAlias.Id));
        Assert.Equal(1, secondAliasCount);
    }
}
