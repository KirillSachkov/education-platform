using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SyncEntityAssetsTests : FileServiceTestsBase
{
    public SyncEntityAssetsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task SyncEntityAssets_RemovesOrphanedAssets_KeepsActiveOnes()
    {
        // Arrange — create 3 markdown images directly bound to lesson entity
        Guid lessonId = Guid.NewGuid();
        var target = new TargetEntityDto("material", lessonId);

        Guid asset1Id = await CreateEntityBoundMarkdownImageAsync(target, "img1.png");
        Guid asset2Id = await CreateEntityBoundMarkdownImageAsync(target, "img2.png");
        Guid asset3Id = await CreateEntityBoundMarkdownImageAsync(target, "img3.png");

        // Act — sync with only asset1 and asset3 as active (asset2 is orphan)
        var syncRequest = new SyncEntityAssetsRequest(
            new TargetEntityDto("material", lessonId),
            ["markdown_image"],
            [asset1Id, asset3Id]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", syncRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<int>? envelope = await response.Content.ReadFromJsonAsync<Envelope<int>>();
        Assert.NotNull(envelope?.Result);
        Assert.Equal(1, envelope.Result); // 1 orphan removed

        await ExecuteInDb(async db =>
        {
            MediaAsset kept1 = await db.MediaAssets.SingleAsync(x => x.Id == asset1Id);
            Assert.Equal(AssetStatus.READY, kept1.Status);

            MediaAsset orphan = await db.MediaAssets.SingleAsync(x => x.Id == asset2Id);
            Assert.Equal(AssetStatus.DELETING, orphan.Status);

            MediaAsset kept2 = await db.MediaAssets.SingleAsync(x => x.Id == asset3Id);
            Assert.Equal(AssetStatus.READY, kept2.Status);
        });
    }

    [Fact]
    public async Task SyncEntityAssets_EmptyActiveIds_MarksAllAsDeleting()
    {
        Guid lessonId = Guid.NewGuid();
        var target = new TargetEntityDto("material", lessonId);

        Guid asset1Id = await CreateEntityBoundMarkdownImageAsync(target, "a.png");
        Guid asset2Id = await CreateEntityBoundMarkdownImageAsync(target, "b.png");

        var syncRequest = new SyncEntityAssetsRequest(
            new TargetEntityDto("material", lessonId),
            ["markdown_image"],
            []);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", syncRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<int>? envelope = await response.Content.ReadFromJsonAsync<Envelope<int>>();
        Assert.Equal(2, envelope!.Result);

        await ExecuteInDb(async db =>
        {
            MediaAsset a1 = await db.MediaAssets.SingleAsync(x => x.Id == asset1Id);
            Assert.Equal(AssetStatus.DELETING, a1.Status);

            MediaAsset a2 = await db.MediaAssets.SingleAsync(x => x.Id == asset2Id);
            Assert.Equal(AssetStatus.DELETING, a2.Status);
        });
    }

    [Fact]
    public async Task SyncEntityAssets_NoOrphans_ReturnsZero()
    {
        Guid lessonId = Guid.NewGuid();
        var target = new TargetEntityDto("material", lessonId);

        Guid assetId = await CreateEntityBoundMarkdownImageAsync(target, "only.png");

        var syncRequest = new SyncEntityAssetsRequest(
            new TargetEntityDto("material", lessonId),
            ["markdown_image"],
            [assetId]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", syncRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<int>? envelope = await response.Content.ReadFromJsonAsync<Envelope<int>>();
        Assert.Equal(0, envelope!.Result);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == assetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
        });
    }

    [Fact]
    public async Task SyncEntityAssets_BothUsageTypesEmpty_NoOpOnExistingMaterial()
    {
        // Backward-compat: pre-existing material/issue без markdown_file ассетов
        // → sync с usageTypes=[markdown_image, markdown_file] и пустым active list
        // должен пройти как no-op (или удалить только markdown_image орфаны),
        // не падая на отсутствии markdown_file записей.
        Guid materialId = Guid.NewGuid();
        var target = new TargetEntityDto("material", materialId);

        // Только markdown_image — представляет старый материал
        Guid imageId = await CreateEntityBoundMarkdownImageAsync(target, "old.png");

        // Sync с обоими usage types, image ОСТАЁТСЯ active, file список пустой
        var syncRequest = new SyncEntityAssetsRequest(
            target,
            ["markdown_image", "markdown_file"],
            [imageId]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", syncRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<int>? envelope = await response.Content.ReadFromJsonAsync<Envelope<int>>();
        Assert.Equal(0, envelope!.Result);

        await ExecuteInDb(async db =>
        {
            MediaAsset image = await db.MediaAssets.SingleAsync(x => x.Id == imageId);
            Assert.Equal(AssetStatus.READY, image.Status);
        });
    }

    [Fact]
    public async Task SyncEntityAssets_InvalidUsageType_ReturnsBadRequest()
    {
        var syncRequest = new SyncEntityAssetsRequest(
            new TargetEntityDto("material", Guid.NewGuid()),
            ["nonexistent_type"],
            []);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", syncRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("avatar")]
    [InlineData("course_preview")]
    [InlineData("course_video")]
    [InlineData("material_preview")]
    [InlineData("material_video")]
    [InlineData("collection_cover")]
    public async Task SyncEntityAssets_SingleSlotUsage_ReturnsBadRequest(string usageType)
    {
        var syncRequest = new SyncEntityAssetsRequest(
            new TargetEntityDto("material", Guid.NewGuid()),
            [usageType],
            []);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", syncRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("asset.sync.single_slot.unsupported", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SyncEntityAssets_EmptyUsageTypes_ReturnsBadRequest()
    {
        var syncRequest = new SyncEntityAssetsRequest(
            new TargetEntityDto("material", Guid.NewGuid()),
            [],
            []);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/assets/sync", syncRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> CreateEntityBoundMarkdownImageAsync(TargetEntityDto target, string fileName)
    {
        byte[] fileContent = [1, 2, 3, 4];

        var request = new InitiateFileUploadRequest(
            FileName: fileName,
            ContentType: "image/png",
            Size: fileContent.Length,
            UsageType: "markdown_image",
            DraftId: null,
            TargetEntity: target);

        HttpResponseMessage initResponse = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        initResponse.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? initEnvelope =
            await initResponse.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        InitiateFileUploadResponse initResult = initEnvelope!.Result!;

        // Upload to S3
        using HttpClient httpClient = new();
        using HttpRequestMessage putRequest = new(HttpMethod.Put, initResult.UploadUrl)
        {
            Content = new ByteArrayContent(fileContent),
        };

        if (initResult.RequiredHeaders.TryGetValue("Content-Type", out string? contentType))
        {
            putRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        foreach ((string key, string value) in initResult.RequiredHeaders)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
            putRequest.Headers.TryAddWithoutValidation(key, value);
        }

        HttpResponseMessage putResponse = await httpClient.SendAsync(putRequest);
        putResponse.EnsureSuccessStatusCode();

        // Complete
        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        completeResponse.EnsureSuccessStatusCode();

        return initResult.AssetId;
    }
}
