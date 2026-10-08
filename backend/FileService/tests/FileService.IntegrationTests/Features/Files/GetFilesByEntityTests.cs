using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Files;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetFilesByEntityTests : FileServiceTestsBase
{
    public GetFilesByEntityTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetFilesByEntity_ReturnsFilesForEntity()
    {
        Guid lessonId = Guid.NewGuid();
        Guid otherLessonId = Guid.NewGuid();
        var target = new TargetEntityDto("material", lessonId);
        var otherTarget = new TargetEntityDto("material", otherLessonId);

        Guid id1 = await CreateReadyFileAsync(target, "img1.png");
        Guid id2 = await CreateReadyFileAsync(target, "img2.png");
        await CreateReadyFileAsync(otherTarget, "other.png"); // belongs to different entity

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/files/by-entity?entityId={lessonId}&entityType=material");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal(2, envelope.Result.Count);
        Assert.All(envelope.Result, f => Assert.Equal(lessonId, f.TargetEntity!.Id));
    }

    [Fact]
    public async Task GetFilesByEntity_ExcludesDeletedFiles()
    {
        Guid lessonId = Guid.NewGuid();
        var target = new TargetEntityDto("material", lessonId);

        Guid activeId = await CreateReadyFileAsync(target, "active.png");
        Guid deletedId = await CreateReadyFileAsync(target, "deleted.png");

        await AppHttpClient.DeleteAsync($"/files/{deletedId}");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/files/by-entity?entityId={lessonId}&entityType=material");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Single(envelope.Result);
        Assert.Equal(activeId, envelope.Result[0].Id);
    }

    [Fact]
    public async Task GetFilesByEntity_NoFilesForEntity_ReturnsEmptyList()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/files/by-entity?entityId={Guid.NewGuid()}&entityType=material");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Empty(envelope.Result);
    }

    private async Task<Guid> CreateReadyFileAsync(TargetEntityDto target, string fileName)
    {
        byte[] content = [1, 2, 3, 4];

        var request = new InitiateFileUploadRequest(
            FileName: fileName,
            ContentType: "image/png",
            Size: content.Length,
            UsageType: "markdown_image",
            DraftId: null,
            TargetEntity: target);

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
