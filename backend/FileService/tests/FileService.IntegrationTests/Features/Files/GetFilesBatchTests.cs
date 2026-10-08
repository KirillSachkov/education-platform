using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Files;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetFilesBatchTests : FileServiceTestsBase
{
    public GetFilesBatchTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetFilesBatch_ReturnsRequestedFiles()
    {
        Guid id1 = await CreateReadyFileAsync(new TargetEntityDto("material", Guid.NewGuid()), "file1.png", "material_preview");
        Guid id2 = await CreateReadyFileAsync(new TargetEntityDto("material", Guid.NewGuid()), "file2.png", "material_preview");
        Guid id3 = await CreateReadyFileAsync(new TargetEntityDto("material", Guid.NewGuid()), "file3.png", "material_preview");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/files/batch/?ids={id1}&ids={id2}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal(2, envelope.Result.Count);
        Assert.Contains(envelope.Result, f => f.Id == id1);
        Assert.Contains(envelope.Result, f => f.Id == id2);
    }

    [Fact]
    public async Task GetFilesBatch_EmptyIds_ReturnsEmptyList()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync("/internal/files/batch/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Empty(envelope.Result);
    }

    [Fact]
    public async Task GetFilesBatch_NonexistentIds_ReturnsEmpty()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/files/batch/?ids={Guid.NewGuid()}&ids={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Empty(envelope.Result);
    }

    [Fact]
    public async Task GetFilesBatch_AnonymousCallerIsDenied_ButInternalServiceCanRead()
    {
        Guid id = await CreateReadyFileAsync(new TargetEntityDto("material", Guid.NewGuid()), "anon.png", "material_preview");

        RemoveAuthentication();

        HttpResponseMessage anonymousResponse = await AppHttpClient.GetAsync($"/internal/files/batch/?ids={id}");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        AuthenticateAs(Guid.CreateVersion7(), "platform-service");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/files/batch/?ids={id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();
        Assert.NotNull(envelope?.Result);
        Assert.Single(envelope.Result);
        Assert.Equal(id, envelope.Result[0].Id);
    }

    [Fact]
    public async Task GetFilesBatch_ExcludesDeletedFiles()
    {
        Guid activeId = await CreateReadyFileAsync(new TargetEntityDto("material", Guid.NewGuid()), "active.png", "material_preview");
        Guid deletedId = await CreateReadyFileAsync(new TargetEntityDto("material", Guid.NewGuid()), "deleted.png", "material_preview");

        // Delete one file
        HttpResponseMessage deleteResponse = await AppHttpClient.DeleteAsync($"/files/{deletedId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/files/batch/?ids={activeId}&ids={deletedId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<GetFileResponse>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<GetFileResponse>>>();

        Assert.NotNull(envelope?.Result);
        Assert.Single(envelope.Result);
        Assert.Equal(activeId, envelope.Result[0].Id);
    }

    private async Task<Guid> CreateReadyFileAsync(TargetEntityDto target, string fileName, string usageType)
    {
        byte[] content = [1, 2, 3, 4];

        var request = new InitiateFileUploadRequest(
            FileName: fileName,
            ContentType: "image/png",
            Size: content.Length,
            UsageType: usageType,
            DraftId: null,
            TargetEntity: target);

        HttpResponseMessage initResponse = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        initResponse.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? initEnvelope =
            await initResponse.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        InitiateFileUploadResponse initResult = initEnvelope!.Result!;

        await UploadToS3Async(initResult.UploadUrl, initResult.RequiredHeaders, content);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        completeResponse.EnsureSuccessStatusCode();

        return initResult.AssetId;
    }

    private async Task UploadToS3Async(
        string uploadUrl,
        IReadOnlyDictionary<string, string> requiredHeaders,
        byte[] content)
    {
        using HttpClient client = new();
        using HttpRequestMessage request = new(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(content),
        };

        if (requiredHeaders.TryGetValue("Content-Type", out string? contentType))
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        foreach ((string key, string value) in requiredHeaders)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            request.Headers.TryAddWithoutValidation(key, value);
        }

        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
