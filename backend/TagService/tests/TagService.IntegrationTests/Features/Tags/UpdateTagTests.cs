using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class UpdateTagTests : TagServiceTestsBase
{
    public UpdateTagTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task UpdateTag_WithValidTitle_ShouldUpdateTitleSlug_AndKeepKind()
    {
        Tag tag = await CreateTagAsync("old-title");

        await ExecuteInDb(async db =>
        {
            Tag dbTag = await db.Tags.FirstAsync(x => x.Id == tag.Id);
            dbTag.MarkAsAlias();
            await db.SaveChangesAsync();
        });

        var request = new UpdateTagRequest { Title = "New Title" };

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/tags/{tag.Id.Value}", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(tag.Id.Value, envelope.Result);

        Tag dbTagAfterUpdate = await ExecuteInDb(db => db.Tags.FirstAsync(x => x.Id == tag.Id));
        Assert.Equal("New Title", dbTagAfterUpdate.Title.Value);
        Assert.Equal(TagSlug.Of("New Title").Value, dbTagAfterUpdate.Slug);
        Assert.Equal(TagKind.ALIAS, dbTagAfterUpdate.Kind);
    }

    [Fact]
    public async Task UpdateTag_WhenNewSlugAlreadyExists_ShouldReturnConflict()
    {
        Tag existingTag = await CreateTagAsync("c-sharp");
        Tag tagToUpdate = await CreateTagAsync("another-title");

        var request = new UpdateTagRequest { Title = existingTag.Title.Value };

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/tags/{tagToUpdate.Id.Value}", request);

        await AssertConflictResponseAsync(
            response,
            "tag.already.exists",
            "Тег с таким названием уже существует");
    }

    [Fact]
    public async Task UpdateTag_WithInvalidTitle_ShouldReturnBadRequest()
    {
        Tag tag = await CreateTagAsync("valid");
        var request = new UpdateTagRequest { Title = string.Empty };

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/tags/{tag.Id.Value}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTag_WithEmptyTagId_ShouldReturnBadRequest()
    {
        var request = new UpdateTagRequest { Title = "updated" };

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/tags/{Guid.Empty}", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTag_WithNonExistentTag_ShouldReturnNotFound()
    {
        var request = new UpdateTagRequest { Title = "updated" };

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/tags/{Guid.NewGuid()}", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
