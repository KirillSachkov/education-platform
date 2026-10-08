using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TagService.Contracts.Tags.Requests;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CreateTagTests : TagServiceTestsBase
{
    public CreateTagTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateTag_WithValidTitle_ShouldCreateTag()
    {
        var request = new CreateTagRequest
        {
            Title = "C#"
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);

        Guid createdTagId = envelope.Result;

        Tag? createdTag = await ExecuteInDb(db => db.Tags.FirstOrDefaultAsync(x => x.Id == TagId.Of(createdTagId)));
        Assert.NotNull(createdTag);
        Assert.Equal("C#", createdTag!.Title.Value);
        Assert.Equal("c#", createdTag.Slug.Value);
    }

    [Fact]
    public async Task CreateTag_WithDuplicateSlug_ShouldReturnConflict()
    {
        await CreateTagAsync("C#");

        var request = new CreateTagRequest
        {
            Title = "c#"
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags", request);

        await AssertConflictResponseAsync(
            response,
            "tag.already.exists",
            "Тег с таким названием уже существует");
    }

    [Fact]
    public async Task CreateTag_WithInvalidTitle_ShouldReturnBadRequest()
    {
        var request = new CreateTagRequest
        {
            Title = string.Empty
        };

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/tags", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void TagCreate_WithEmptyAuthorId_ShouldReturnFailure()
    {
        Result<Tag, Error> result = Tag.Create(
            TagTitle.Of("valid-title").Value,
            TagSlug.Of("valid-title").Value,
            Guid.Empty);

        Assert.True(result.IsFailure);
    }
}
