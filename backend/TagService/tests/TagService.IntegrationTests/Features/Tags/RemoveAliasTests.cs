using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RemoveAliasTests : TagServiceTestsBase
{
    public RemoveAliasTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task RemoveAlias_WhenAliasHasNoOtherReferences_ShouldRestoreRegularKind()
    {
        Tag canonicalTag = await CreateTagAsync("C#");
        Tag aliasTag = await CreateTagAsync("c-sharp");

        await ExecuteInDb(async db =>
        {
            Tag dbAliasTag = await db.Tags.FirstAsync(x => x.Id == aliasTag.Id);
            dbAliasTag.MarkAsAlias();
            await db.SaveChangesAsync();
        });

        await CreateAliasAsync(canonicalTag.Id.Value, aliasTag.Id.Value);

        var request = new RemoveAliasRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(canonicalTag.Id.Value, envelope.Result);

        int aliasesCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x => x.TagId == canonicalTag.Id && x.AliasTagId == aliasTag.Id));
        Assert.Equal(0, aliasesCount);

        Tag dbAliasAfterRemove = await ExecuteInDb(db => db.Tags.FirstAsync(x => x.Id == aliasTag.Id));
        Assert.Equal(TagKind.CANON, dbAliasAfterRemove.Kind);
    }

    [Fact]
    public async Task RemoveAlias_WhenAliasBelongsToAnotherCanonical_ShouldReturnBadRequest()
    {
        Tag requestedCanonical = await CreateTagAsync("requested");
        Tag actualCanonical = await CreateTagAsync("actual");
        Tag aliasTag = await CreateTagAsync("alias");

        await ExecuteInDb(async db =>
        {
            Tag dbAliasTag = await db.Tags.FirstAsync(x => x.Id == aliasTag.Id);
            dbAliasTag.MarkAsAlias();
            await db.SaveChangesAsync();
        });
        await CreateAliasAsync(actualCanonical.Id.Value, aliasTag.Id.Value);

        HttpResponseMessage response = await DeleteAsJsonAsync(
            $"/tags/{requestedCanonical.Id.Value}/aliases",
            new RemoveAliasRequest { TagIds = [aliasTag.Id.Value] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        int aliasesCount = await ExecuteInDb(db =>
            db.TagAliases.CountAsync(x => x.TagId == actualCanonical.Id && x.AliasTagId == aliasTag.Id));
        Assert.Equal(1, aliasesCount);

        Tag dbAliasAfterRequest = await ExecuteInDb(db => db.Tags.FirstAsync(x => x.Id == aliasTag.Id));
        Assert.Equal(TagKind.ALIAS, dbAliasAfterRequest.Kind);
    }

    [Fact]
    public async Task RemoveAlias_WithDuplicateAliasIds_ShouldReturnBadRequest()
    {
        Tag canonicalTag = await CreateTagAsync("java");
        Tag aliasTag = await CreateTagAsync("jdk");

        var request = new RemoveAliasRequest
        {
            TagIds = [aliasTag.Id.Value, aliasTag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAlias_WithEmptyAliasIds_ShouldReturnBadRequest()
    {
        Tag canonicalTag = await CreateTagAsync("rust");

        var request = new RemoveAliasRequest
        {
            TagIds = []
        };

        HttpResponseMessage response = await DeleteAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAlias_WithSelfAlias_ShouldReturnBadRequest()
    {
        Tag canonicalTag = await CreateTagAsync("scala");

        var request = new RemoveAliasRequest
        {
            TagIds = [canonicalTag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync($"/tags/{canonicalTag.Id.Value}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAlias_WithEmptyTagId_ShouldReturnBadRequest()
    {
        Tag aliasTag = await CreateTagAsync("nestjs");

        var request = new RemoveAliasRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync($"/tags/{Guid.Empty}/aliases", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveAlias_WithNonExistentCanonicalTag_ShouldReturnNotFound()
    {
        Tag aliasTag = await CreateTagAsync("grpc");

        var request = new RemoveAliasRequest
        {
            TagIds = [aliasTag.Id.Value]
        };

        HttpResponseMessage response = await DeleteAsJsonAsync($"/tags/{Guid.NewGuid()}/aliases", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
