using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetDraftAssetsTests : FileServiceTestsBase
{
    public GetDraftAssetsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetDraftAssets_ReturnsDraftFilesByDraftId()
    {
        Guid draftId = Guid.NewGuid();
        Guid otherDraftId = Guid.NewGuid();

        Guid id1 = await CreateDraftMarkdownImageAsync(draftId, "img1.png");
        Guid id2 = await CreateDraftMarkdownImageAsync(draftId, "img2.png");
        await CreateDraftMarkdownImageAsync(otherDraftId, "other.png"); // different draft

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/draft-assets?draftId={draftId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal(2, envelope.Result.Count);
        Assert.All(envelope.Result, f => Assert.True(f.IsTemporary));
        Assert.Contains(envelope.Result, f => f.Id == id1);
        Assert.Contains(envelope.Result, f => f.Id == id2);
    }

    [Fact]
    public async Task GetDraftAssets_ExcludesDeletedDrafts()
    {
        Guid draftId = Guid.NewGuid();

        Guid activeId = await CreateDraftMarkdownImageAsync(draftId, "active.png");
        Guid deletedId = await CreateDraftMarkdownImageAsync(draftId, "deleted.png");

        // Delete one draft file
        await AppHttpClient.DeleteAsync($"/files/{deletedId}");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/draft-assets?draftId={draftId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Single(envelope.Result);
        Assert.Equal(activeId, envelope.Result[0].Id);
    }

    [Fact]
    public async Task GetDraftAssets_NoDrafts_ReturnsEmptyList()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/draft-assets?draftId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Empty(envelope.Result);
    }

    private async Task<Guid> CreateDraftMarkdownImageAsync(Guid draftId, string fileName)
    {
        byte[] content = [1, 2, 3, 4];

        var request = new InitiateFileUploadRequest(
            FileName: fileName,
            ContentType: "image/png",
            Size: content.Length,
            UsageType: "markdown_image",
            DraftId: draftId,
            TargetEntity: null);

        HttpResponseMessage initResponse = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        initResponse.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? initEnvelope =
            await initResponse.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        InitiateFileUploadResponse initResult = initEnvelope!.Result!;

        using HttpClient httpClient = new();
        using HttpRequestMessage putRequest = new(HttpMethod.Put, initResult.UploadUrl)
        {
            Content = new ByteArrayContent(content),
        };

        if (initResult.RequiredHeaders.TryGetValue("Content-Type", out string? ct))
            putRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(ct);

        foreach ((string key, string value) in initResult.RequiredHeaders)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            putRequest.Headers.TryAddWithoutValidation(key, value);
        }

        (await httpClient.SendAsync(putRequest)).EnsureSuccessStatusCode();

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        completeResponse.EnsureSuccessStatusCode();

        return initResult.AssetId;
    }
}
