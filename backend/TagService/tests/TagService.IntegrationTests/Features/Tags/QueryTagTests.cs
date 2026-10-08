using System.Net;
using System.Net.Http.Json;
using SharedKernel;
using TagService.Contracts.Tags;
using TagService.Contracts.Tags.Dtos;
using TagService.Domain.Tags;
using TagService.IntegrationTests.Infrastructure;

namespace TagService.IntegrationTests.Features.Tags;

/// <summary>
/// Integration tests for all TagService read endpoints:
/// GET /tags, GET /tags/{id}, GET /tags/batch, GET /tags/entity,
/// GET /tags/{id}/aliases, GET /tags/suggest, GET /tags/popular.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class QueryTagTests : TagServiceTestsBase
{
    public QueryTagTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ─── GET /tags ───

    [Fact]
    public async Task GetTags_EmptyDb_ShouldReturnEmptyPage()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<CursorResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(0, envelope.Result!.TotalCount);
        Assert.Empty(envelope.Result.Items);
        Assert.Null(envelope.Result.NextCursor);
    }

    [Fact]
    public async Task GetTags_WithSeededTags_ShouldReturnFirstPageAndCursor()
    {
        await CreateTagAsync("alpha");
        await CreateTagAsync("beta");
        await CreateTagAsync("gamma");

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags?limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<CursorResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(3, envelope.Result!.TotalCount);
        Assert.Equal(2, envelope.Result.Items.Count);
        Assert.Equal("alpha", envelope.Result.Items[0].Title);
        Assert.Equal("beta", envelope.Result.Items[1].Title);
        Assert.NotNull(envelope.Result.NextCursor);
    }

    [Fact]
    public async Task GetTags_WithCursor_ShouldContinueFromBookmark()
    {
        await CreateTagAsync("alpha");
        await CreateTagAsync("beta");
        await CreateTagAsync("gamma");

        RemoveAuthentication();

        HttpResponseMessage firstResponse = await AppHttpClient.GetAsync("/tags?limit=2");
        Envelope<CursorResponse<TagDto>>? firstEnvelope =
            await firstResponse.Content.ReadFromJsonAsync<Envelope<CursorResponse<TagDto>>>();

        Assert.NotNull(firstEnvelope?.Result?.NextCursor);
        string cursor = Uri.EscapeDataString(firstEnvelope.Result.NextCursor);

        HttpResponseMessage secondResponse = await AppHttpClient.GetAsync($"/tags?limit=2&cursor={cursor}");
        Envelope<CursorResponse<TagDto>>? secondEnvelope =
            await secondResponse.Content.ReadFromJsonAsync<Envelope<CursorResponse<TagDto>>>();

        Assert.NotNull(secondEnvelope);
        Assert.Single(secondEnvelope.Result!.Items);
        Assert.Equal("gamma", secondEnvelope.Result.Items[0].Title);
        Assert.Null(secondEnvelope.Result.NextCursor);
    }

    [Fact]
    public async Task GetTags_WithMalformedCursor_ShouldReturnBadRequest()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags?limit=10&cursor=not-base64");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTags_WithSearchFilter_ShouldReturnOnlyMatches()
    {
        await CreateTagAsync("python");
        await CreateTagAsync("pytorch");
        await CreateTagAsync("rust");

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags?limit=10&search=py");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<CursorResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(2, envelope.Result!.TotalCount);
        Assert.All(envelope.Result.Items, t => Assert.StartsWith("py", t.Title, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetTags_WithAuthorIdFilter_ShouldReturnOnlyThatAuthorsTags()
    {
        Guid otherAuthor = Guid.NewGuid();
        await CreateTagAsync("mine-1", authorId: TestAuthorId);
        await CreateTagAsync("mine-2", authorId: TestAuthorId);
        await CreateTagAsync("theirs", authorId: otherAuthor);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/tags?limit=10&authorId={TestAuthorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<CursorResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(2, envelope.Result!.TotalCount);
        Assert.All(envelope.Result.Items, t => Assert.StartsWith("mine-", t.Title, StringComparison.Ordinal));
    }

    // ─── GET /tags/{id} ───

    [Fact]
    public async Task GetTagById_ExistingTag_ShouldReturnTag()
    {
        Tag tag = await CreateTagAsync("docker");

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/tags/{tag.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<TagDto>? envelope = await response.Content.ReadFromJsonAsync<Envelope<TagDto>>();

        Assert.NotNull(envelope);
        Assert.Equal(tag.Id.Value, envelope.Result!.Id);
        Assert.Equal("docker", envelope.Result.Title);
    }

    [Fact]
    public async Task GetTagById_NonExistent_ShouldReturnNotFound()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/tags/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── GET /tags/batch ───

    [Fact]
    public async Task GetTagsBatch_WithMixedIds_ShouldReturnOnlyExisting()
    {
        Tag tagA = await CreateTagAsync("redis");
        Tag tagB = await CreateTagAsync("postgres");
        Guid missing = Guid.NewGuid();

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient
            .GetAsync($"/tags/batch?tagIds={tagA.Id.Value}&tagIds={tagB.Id.Value}&tagIds={missing}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(2, envelope.Result!.Count);
        Assert.Contains(envelope.Result, t => t.Id == tagA.Id.Value);
        Assert.Contains(envelope.Result, t => t.Id == tagB.Id.Value);
    }

    [Fact]
    public async Task GetTagsBatch_EmptyTagIds_ShouldReturnBadRequest()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags/batch");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ─── GET /tags/entity ───

    [Fact]
    public async Task GetEntityTags_WithLinkedTags_ShouldReturnTagsForEntity()
    {
        Tag tagA = await CreateTagAsync("k8s");
        Tag tagB = await CreateTagAsync("helm");
        await CreateLinkAsync(tagA.Id.Value);
        await CreateLinkAsync(tagB.Id.Value);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient
            .GetAsync($"/tags/entity?entityType={TestEntityType}&entityId={TestEntityId}&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(2, envelope.Result!.TotalCount);
        Assert.Equal(2, envelope.Result.Items.Count);
    }

    [Fact]
    public async Task GetEntityTags_NoLinks_ShouldReturnEmptyPage()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient
            .GetAsync($"/tags/entity?entityType={TestEntityType}&entityId={Guid.NewGuid()}&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(0, envelope.Result!.TotalCount);
        Assert.Empty(envelope.Result.Items);
    }

    [Fact]
    public async Task GetEntityTags_WithDifferentEntityTypeCasing_ShouldReturnLinks()
    {
        Tag tag = await CreateTagAsync("case-normalized");
        await CreateLinkAsync(tag.Id.Value);
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/tags/entity?entityType=Material&entityId={TestEntityId}&page=1&pageSize=10");

        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.Result!.TotalCount);
        Assert.Single(envelope.Result.Items);
    }

    [Fact]
    public async Task GetEntityTags_WhenPageIsPastEnd_ShouldPreserveTotalCount()
    {
        Tag tag = await CreateTagAsync("past-end-entity");
        await CreateLinkAsync(tag.Id.Value);
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/tags/entity?entityType={TestEntityType}&entityId={TestEntityId}&page=2&pageSize=1");
        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.Result!.TotalCount);
        Assert.Empty(envelope.Result.Items);
    }

    // ─── GET /tags/{id}/aliases ───

    [Fact]
    public async Task GetTagAliases_WithAliases_ShouldReturnAliases()
    {
        Tag canonical = await CreateTagAsync("javascript");
        Tag alias1 = await CreateTagAsync("js");
        Tag alias2 = await CreateTagAsync("ecmascript");
        await CreateAliasAsync(canonical.Id.Value, alias1.Id.Value);
        await CreateAliasAsync(canonical.Id.Value, alias2.Id.Value);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient
            .GetAsync($"/tags/{canonical.Id.Value}/aliases?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(2, envelope.Result!.TotalCount);
    }

    [Fact]
    public async Task GetTagAliases_NoAliases_ShouldReturnEmptyPage()
    {
        Tag canonical = await CreateTagAsync("standalone");

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient
            .GetAsync($"/tags/{canonical.Id.Value}/aliases?page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(0, envelope.Result!.TotalCount);
    }

    [Fact]
    public async Task GetTagAliases_WhenPageIsPastEnd_ShouldPreserveTotalCount()
    {
        Tag canonical = await CreateTagAsync("aliases-past-end");
        Tag alias = await CreateTagAsync("aliases-past-end-alias");
        await CreateAliasAsync(canonical.Id.Value, alias.Id.Value);
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/tags/{canonical.Id.Value}/aliases?page=2&pageSize=1");
        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.Result!.TotalCount);
        Assert.Empty(envelope.Result.Items);
    }

    // ─── GET /tags/suggest ───

    [Fact]
    public async Task SuggestTags_WithSearchPrefix_ShouldReturnCanonicalMatches()
    {
        await CreateTagAsync("react");
        await CreateTagAsync("redux");
        await CreateTagAsync("angular");

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient
            .GetAsync("/tags/suggest?page=1&pageSize=10&search=re");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(2, envelope.Result!.TotalCount);
    }

    [Fact]
    public async Task SuggestTags_WithAuthorIdFilter_ShouldReturnOnlyThatAuthorsTags()
    {
        Guid otherAuthor = Guid.NewGuid();
        await CreateTagAsync("my-lib", authorId: TestAuthorId);
        await CreateTagAsync("their-lib", authorId: otherAuthor);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient
            .GetAsync($"/tags/suggest?page=1&pageSize=10&authorId={TestAuthorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.Result!.TotalCount);
        Assert.Equal("my-lib", envelope.Result.Items[0].Title);
    }

    [Fact]
    public async Task SuggestTags_WhenPageIsPastEnd_ShouldPreserveTotalCount()
    {
        await CreateTagAsync("suggest-past-end");
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/tags/suggest?page=2&pageSize=1&search=suggest-past-end");
        Envelope<PaginationResponse<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PaginationResponse<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.Equal(1, envelope.Result!.TotalCount);
        Assert.Empty(envelope.Result.Items);
    }

    // ─── GET /tags/popular ───

    [Fact]
    public async Task GetPopularTags_EmptyDb_ShouldReturnEmptyList()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags/popular?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
    }

    [Fact]
    public async Task GetPopularTags_WithLinkedTags_ShouldReturnOrderedAndLimitedList()
    {
        Tag alpha = await CreateTagAsync("alpha");
        Tag beta = await CreateTagAsync("beta");
        Tag gamma = await CreateTagAsync("gamma");

        await CreateLinkAsync(alpha.Id.Value, entityId: TestEntityId);
        await CreateLinkAsync(alpha.Id.Value, entityId: SecondEntityId);
        await CreateLinkAsync(alpha.Id.Value, entityId: Guid.NewGuid());
        await CreateLinkAsync(beta.Id.Value, entityId: Guid.NewGuid());
        await CreateLinkAsync(beta.Id.Value, entityId: Guid.NewGuid());
        await CreateLinkAsync(gamma.Id.Value, entityId: Guid.NewGuid());

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/tags/popular?limit=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Equal(2, envelope.Result.Count);
        Assert.Equal("alpha", envelope.Result[0].Title);
        Assert.Equal("beta", envelope.Result[1].Title);
    }

    [Fact]
    public async Task GetPopularTags_WithAuthorIdFilter_ShouldReturnOnlyThatAuthorsTags()
    {
        Guid otherAuthor = Guid.CreateVersion7();
        Tag mine = await CreateTagAsync("mine-popular", authorId: TestAuthorId);
        Tag mineSecond = await CreateTagAsync("mine-second", authorId: TestAuthorId);
        Tag theirs = await CreateTagAsync("their-popular", authorId: otherAuthor);

        await CreateLinkAsync(mine.Id.Value, entityId: TestEntityId);
        await CreateLinkAsync(mine.Id.Value, entityId: SecondEntityId);
        await CreateLinkAsync(mineSecond.Id.Value, entityId: Guid.CreateVersion7());
        await CreateLinkAsync(theirs.Id.Value, entityId: Guid.CreateVersion7());
        await CreateLinkAsync(theirs.Id.Value, entityId: Guid.CreateVersion7());
        await CreateLinkAsync(theirs.Id.Value, entityId: Guid.CreateVersion7());

        RemoveAuthentication();

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/tags/popular?limit=7&authorId={TestAuthorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<IReadOnlyList<TagDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<TagDto>>>();

        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Equal(2, envelope.Result.Count);
        Assert.Equal("mine-popular", envelope.Result[0].Title);
        Assert.Equal("mine-second", envelope.Result[1].Title);
    }
}
