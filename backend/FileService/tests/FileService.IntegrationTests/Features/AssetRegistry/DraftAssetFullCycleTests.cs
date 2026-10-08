using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.AssetRegistry;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class DraftAssetFullCycleTests : FileServiceTestsBase
{
    public DraftAssetFullCycleTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task MarkdownImage_FullDraftCycle_InitiateUploadCompleteBind_PublishesBoundEvent()
    {
        // Arrange
        Guid draftId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        byte[] fileContent = [1, 2, 3, 4];

        // Phase 1 — initiate draft upload (no targetEntity)
        var request = new InitiateFileUploadRequest(
            FileName: "diagram.png",
            ContentType: "image/png",
            Size: fileContent.Length,
            UsageType: "markdown_image",
            DraftId: draftId,
            TargetEntity: null);

        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(request);

        // Assert — draft asset is temporary, no target entity
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.PENDING_UPLOAD, asset.Status);
            Assert.Equal(AssetUsageType.MARKDOWN_IMAGE, asset.UsageType);
            Assert.True(asset.IsTemporary);
            Assert.Equal(draftId, asset.DraftId);
            Assert.Null(asset.TargetEntity);
        });

        // Phase 2 — upload to S3
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, fileContent);

        // Phase 3 — complete upload (NO FileAssetBound expected — draft has no target)
        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.True(asset.IsTemporary);
            Assert.Null(asset.TargetEntity);
        });

        // Phase 4 — bind draft to lesson entity (FileAssetBound expected)
        OutboxCollector.Clear();

        HttpResponseMessage bindResponse = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", lessonId),
                [initiateResult.AssetId]));

        Assert.Equal(HttpStatusCode.OK, bindResponse.StatusCode);

        // Assert — FileAssetBound published
        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("file", published.Kind);
        Assert.Equal("markdown_image", published.UsageType);
        Assert.Equal(lessonId, published.TargetEntityId);
        Assert.Equal("material", published.TargetEntityType);

        // Assert — asset is now bound, no longer temporary
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
            Assert.False(asset.IsTemporary);
            Assert.Null(asset.DraftId);
            Assert.NotNull(asset.TargetEntity);
            Assert.Equal(lessonId, asset.TargetEntity.Id);
            Assert.Equal("material", asset.TargetEntity.Type);
        });
    }

    [Fact]
    public async Task MarkdownImage_DraftBoundToIssue_PublishesBoundEventWithIssueTarget()
    {
        Guid draftId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        InitiateFileUploadResponse file = await InitiateAndCompleteDraftFileAsync(draftId, "screenshot.png");

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("issue", issueId),
                [file.AssetId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal("markdown_image", published.UsageType);
        Assert.Equal(issueId, published.TargetEntityId);
        Assert.Equal("issue", published.TargetEntityType);
    }

    [Fact]
    public async Task MarkdownImage_DraftBoundToMaterial_PublishesBoundEventWithMaterialTarget()
    {
        Guid draftId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        InitiateFileUploadResponse file = await InitiateAndCompleteDraftFileAsync(draftId, "chart.png");

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", materialId),
                [file.AssetId]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal("markdown_image", published.UsageType);
        Assert.Equal(materialId, published.TargetEntityId);
        Assert.Equal("material", published.TargetEntityType);
    }

    [Fact]
    public async Task MarkdownImage_MultipleDrafts_BindSubset_UnboundStayTemporary()
    {
        Guid draftId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        InitiateFileUploadResponse kept = await InitiateAndCompleteDraftFileAsync(draftId, "kept.png");
        InitiateFileUploadResponse removed = await InitiateAndCompleteDraftFileAsync(draftId, "removed.png");
        InitiateFileUploadResponse alsoKept = await InitiateAndCompleteDraftFileAsync(draftId, "also-kept.png");

        HttpResponseMessage bindResponse = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", lessonId),
                [kept.AssetId, alsoKept.AssetId]));

        Assert.Equal(HttpStatusCode.OK, bindResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset keptAsset = await db.MediaAssets.SingleAsync(x => x.Id == kept.AssetId);
            Assert.False(keptAsset.IsTemporary);
            Assert.Null(keptAsset.DraftId);

            MediaAsset alsoKeptAsset = await db.MediaAssets.SingleAsync(x => x.Id == alsoKept.AssetId);
            Assert.False(alsoKeptAsset.IsTemporary);
            Assert.Null(alsoKeptAsset.DraftId);

            MediaAsset removedAsset = await db.MediaAssets.SingleAsync(x => x.Id == removed.AssetId);
            Assert.True(removedAsset.IsTemporary);
            Assert.Equal(draftId, removedAsset.DraftId);
        });
    }

    [Fact]
    public async Task MarkdownImage_FullCycleWithDelete_BindThenDelete_PublishesBothEvents()
    {
        Guid draftId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();

        InitiateFileUploadResponse file = await InitiateAndCompleteDraftFileAsync(draftId, "to-delete.png");

        // Bind
        OutboxCollector.Clear();

        HttpResponseMessage bindResponse = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("material", lessonId),
                [file.AssetId]));
        bindResponse.EnsureSuccessStatusCode();

        FileAssetBound boundEvent = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(lessonId, boundEvent.TargetEntityId);

        // Delete
        OutboxCollector.Clear();

        HttpResponseMessage deleteResponse = await AppHttpClient.DeleteAsync($"/files/{file.AssetId}");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        FileAssetDeleted deletedEvent = OutboxCollector.OfType<FileAssetDeleted>().Single();
        Assert.Equal(file.AssetId, deletedEvent.AssetId);
        Assert.Equal("file", deletedEvent.Kind);
        Assert.Equal("markdown_image", deletedEvent.UsageType);
        Assert.Equal(lessonId, deletedEvent.TargetEntityId);
        Assert.Equal("material", deletedEvent.TargetEntityType);

        // Phase 1 leaves the asset DELETING; phase 2 retention finalizes it.
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == file.AssetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssetRetentionService retention = scope.ServiceProvider.GetRequiredService<AssetRetentionService>();
        await retention.ProcessDeletingAssetsAsync(CancellationToken.None);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == file.AssetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    [Fact]
    public async Task MarkdownImage_InitiateWithTargetEntity_ReturnsBadRequest()
    {
        var request = new InitiateFileUploadRequest(
            FileName: "invalid.png",
            ContentType: "image/png",
            Size: 4,
            UsageType: "markdown_image",
            DraftId: Guid.NewGuid(),
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MarkdownImage_BindToInvalidEntityType_ReturnsBadRequest()
    {
        Guid draftId = Guid.NewGuid();
        InitiateFileUploadResponse file = await InitiateAndCompleteDraftFileAsync(draftId, "test.png");

        // markdown_image allows issue, material — NOT "user"
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/draft-assets/bind",
            new BindDraftAssetsRequest(
                draftId,
                new TargetEntityDto("user", Guid.NewGuid()),
                [file.AssetId]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<InitiateFileUploadResponse> InitiateAndCompleteDraftFileAsync(Guid draftId, string fileName)
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(
            new InitiateFileUploadRequest(
                FileName: fileName,
                ContentType: "image/png",
                Size: 4,
                UsageType: "markdown_image",
                DraftId: draftId,
                TargetEntity: null));

        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        completeResponse.EnsureSuccessStatusCode();
        return initiateResult;
    }

    private async Task<InitiateFileUploadResponse> InitiateFileUploadAsync(InitiateFileUploadRequest request)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        return envelope!.Result!;
    }

    private async Task UploadToDirectUrlAsync(
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
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(key, value);
        }

        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
