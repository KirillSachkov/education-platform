using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Core.FilesStorage;
using FileService.Core.Messaging;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using SkiaSharp;

namespace FileService.IntegrationTests.Features.Files;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ImageVariantTests : FileServiceTestsBase
{
    public ImageVariantTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── L2: complete publishes the generate message ─────────────────────────

    [Fact]
    public async Task Complete_ImageAsset_PublishesGenerateImageVariants()
    {
        byte[] png = CreatePng(1000, 600);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "cover.png");

        OutboxCollector.Clear();
        HttpResponseMessage complete = await CompleteAsync(asset.AssetId);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        GenerateImageVariants published = OutboxCollector.OfType<GenerateImageVariants>().Single();
        Assert.Equal(asset.AssetId, published.AssetId);
    }

    [Fact]
    public async Task Complete_NonImageAsset_DoesNotPublishGenerateImageVariants()
    {
        byte[] pdf = "%PDF-1.4 fake"u8.ToArray();
        var request = new InitiateFileUploadRequest(
            FileName: "doc.pdf",
            ContentType: "application/pdf",
            Size: pdf.Length,
            UsageType: "markdown_file",
            DraftId: Guid.NewGuid(),
            TargetEntity: null);

        InitiateFileUploadResponse asset = await InitiateAsync(request);
        await UploadToDirectUrlAsync(asset.UploadUrl, asset.RequiredHeaders, pdf);

        OutboxCollector.Clear();
        HttpResponseMessage complete = await CompleteAsync(asset.AssetId);
        complete.EnsureSuccessStatusCode();

        Assert.Empty(OutboxCollector.OfType<GenerateImageVariants>());
    }

    // ── L1: generation handler records variants + uploads objects ───────────

    [Fact]
    public async Task GenerateHandler_LargeImage_RecordsVariantsAndUploadsObjects()
    {
        byte[] png = CreatePng(1280, 720);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "big.png");
        await CompleteAsync(asset.AssetId);

        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));

        IReadOnlyList<ImageVariant> variants = await GetVariantsAsync(asset.AssetId);

        // Original is 1280 → only 320, 640, 960 (1280 is not < 1280).
        Assert.Equal(new[] { 320, 640, 960 }, variants.Select(v => v.Width));
        Assert.All(variants, v => Assert.Equal("image/webp", v.ContentType));
        Assert.All(variants, v => Assert.True(v.Size > 0));

        // Variant objects physically exist in MinIO.
        foreach (ImageVariant variant in variants)
        {
            Result<ObjectStorageObjectMetadata, Error> meta = await GetObjectMetadataAsync(variant.StorageKey);
            Assert.True(meta.IsSuccess, $"variant object {variant.StorageKey} should exist");
        }
    }

    [Fact]
    public async Task GenerateHandler_SmallImage_GeneratesNoVariants()
    {
        // 200px original is below the smallest bucket (320) → only-downscale ⇒ none.
        byte[] png = CreatePng(200, 150);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "tiny.png");
        await CompleteAsync(asset.AssetId);

        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));

        IReadOnlyList<ImageVariant> variants = await GetVariantsAsync(asset.AssetId);
        Assert.Empty(variants);
    }

    [Fact]
    public async Task GenerateHandler_IsIdempotent_OnReInvoke()
    {
        byte[] png = CreatePng(960, 540);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "mid.png");
        await CompleteAsync(asset.AssetId);

        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));
        IReadOnlyList<ImageVariant> first = await GetVariantsAsync(asset.AssetId);

        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));
        IReadOnlyList<ImageVariant> second = await GetVariantsAsync(asset.AssetId);

        // Same widths + same deterministic keys — re-run safe, no duplicates.
        Assert.Equal(first.Select(v => v.Width), second.Select(v => v.Width));
        Assert.Equal(first.Select(v => v.StorageKey), second.Select(v => v.StorageKey));
    }

    // ── serving: ?w= ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetContent_WithWidth_RedirectsToWebpVariant()
    {
        byte[] png = CreatePng(1000, 600);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "serve.png");
        await CompleteAsync(asset.AssetId);
        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));

        using HttpClient noRedirect = CreateAppClient(allowAutoRedirect: false);

        HttpResponseMessage response = await noRedirect.GetAsync($"/files/{asset.AssetId}/content?w=320");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location!.ToString();
        Assert.Contains($"variants/{asset.AssetId:N}_320.webp", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetContent_WithoutWidth_RedirectsToOriginal()
    {
        byte[] png = CreatePng(1000, 600);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "orig.png");
        await CompleteAsync(asset.AssetId);
        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));

        using HttpClient noRedirect = CreateAppClient(allowAutoRedirect: false);

        HttpResponseMessage response = await noRedirect.GetAsync($"/files/{asset.AssetId}/content");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location!.ToString();
        Assert.Contains($"files/{asset.AssetId:N}.png", location, StringComparison.Ordinal);
        Assert.DoesNotContain("variants", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetContent_WidthAboveOriginal_FallsBackToOriginal()
    {
        // Original 1000px → no 1280 variant. ?w=4000 clamps to 1280, no match → original.
        byte[] png = CreatePng(1000, 600);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "fallback.png");
        await CompleteAsync(asset.AssetId);
        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));

        using HttpClient noRedirect = CreateAppClient(allowAutoRedirect: false);

        HttpResponseMessage response = await noRedirect.GetAsync($"/files/{asset.AssetId}/content?w=4000");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location!.ToString();
        Assert.Contains($"files/{asset.AssetId:N}.png", location, StringComparison.Ordinal);
        Assert.DoesNotContain("variants", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetContent_NoVariantsYet_FallsBackToOriginal()
    {
        // Completed but generation not yet run → ?w= falls back to original.
        byte[] png = CreatePng(1000, 600);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "pending.png");
        await CompleteAsync(asset.AssetId);

        using HttpClient noRedirect = CreateAppClient(allowAutoRedirect: false);

        HttpResponseMessage response = await noRedirect.GetAsync($"/files/{asset.AssetId}/content?w=320");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        string location = response.Headers.Location!.ToString();
        Assert.Contains($"files/{asset.AssetId:N}.png", location, StringComparison.Ordinal);
        Assert.DoesNotContain("variants", location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetContent_NonNumericWidth_FallsBackToOriginal()
    {
        byte[] png = CreatePng(1000, 600);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "badw.png");
        await CompleteAsync(asset.AssetId);
        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));

        using HttpClient noRedirect = CreateAppClient(allowAutoRedirect: false);

        HttpResponseMessage response = await noRedirect.GetAsync($"/files/{asset.AssetId}/content?w=abc");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain("variants", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    // ── backfill: repo batch query + generation (CLI inner loop) ─────────────

    [Fact]
    public async Task Backfill_GeneratesVariantsForExistingReadyImageLackingThem()
    {
        // Complete WITHOUT running generation → a Ready image asset with no variants,
        // exactly what `generate-image-variants` backfills.
        byte[] png = CreatePng(1000, 600);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "backfill.png");
        await CompleteAsync(asset.AssetId);

        Assert.Empty(await GetVariantsAsync(asset.AssetId));

        await using AsyncServiceScope scope = Services.CreateAsyncScope();

        // Repo batch query (translatable READY FILE predicate) returns the asset, and it
        // is image-eligible (in-app narrowing the CLI applies).
        var repository = scope.ServiceProvider
            .GetRequiredService<FileService.Core.Repositories.IMediaAssetRepository>();
        List<MediaAsset> batch = await repository.GetReadyFileAssetsBatchAsync(
            100, DateTime.MinValue.ToUniversalTime(), CancellationToken.None);
        MediaAsset? found = batch.SingleOrDefault(a => a.Id == asset.AssetId);
        Assert.NotNull(found);
        Assert.True(found.IsImageVariantEligible());

        // Generation (CLI inner loop) records the variants.
        var generation = scope.ServiceProvider
            .GetRequiredService<FileService.Core.Services.Files.ImageVariantGenerationService>();
        Result<int, Error> result = await generation.GenerateAsync(asset.AssetId, CancellationToken.None);
        Assert.True(result.IsSuccess);

        Assert.Equal(new[] { 320, 640, 960 }, (await GetVariantsAsync(asset.AssetId)).Select(v => v.Width));
    }

    // ── cleanup on delete ───────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesVariantObjects()
    {
        byte[] png = CreatePng(1280, 720);
        InitiateFileUploadResponse asset = await UploadImageAsync(png, "image/png", "del.png");
        await CompleteAsync(asset.AssetId);
        await InvokeMessageAndWaitAsync(new GenerateImageVariants(asset.AssetId));

        IReadOnlyList<ImageVariant> variants = await GetVariantsAsync(asset.AssetId);
        Assert.NotEmpty(variants);

        // Phase 1: request delete; Phase 2: retention sweep performs physical deletes.
        HttpResponseMessage delete = await AppHttpClient.DeleteAsync($"/files/{asset.AssetId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            AssetRetentionService retention =
                scope.ServiceProvider.GetRequiredService<AssetRetentionService>();
            await retention.ProcessDeletingAssetsAsync(CancellationToken.None);
        }

        // Variant objects are gone from MinIO.
        foreach (ImageVariant variant in variants)
        {
            Result<ObjectStorageObjectMetadata, Error> meta = await GetObjectMetadataAsync(variant.StorageKey);
            Assert.True(meta.IsFailure, $"variant object {variant.StorageKey} should be deleted");
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private static byte[] CreatePng(int width, int height)
    {
        using var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        pixels.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(pixels);
        using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    private async Task<InitiateFileUploadResponse> UploadImageAsync(byte[] content, string contentType, string fileName)
    {
        var request = new InitiateFileUploadRequest(
            FileName: fileName,
            ContentType: contentType,
            Size: content.Length,
            UsageType: "markdown_image",
            DraftId: Guid.NewGuid(),
            TargetEntity: null);

        InitiateFileUploadResponse asset = await InitiateAsync(request);
        await UploadToDirectUrlAsync(asset.UploadUrl, asset.RequiredHeaders, content);
        return asset;
    }

    private async Task<InitiateFileUploadResponse> InitiateAsync(InitiateFileUploadRequest request)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        response.EnsureSuccessStatusCode();
        Envelope<InitiateFileUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();
        return envelope!.Result!;
    }

    private Task<HttpResponseMessage> CompleteAsync(Guid assetId) =>
        AppHttpClient.PostAsJsonAsync($"/files/{assetId}/complete", new CompleteFileUploadRequest(null));

    private async Task<IReadOnlyList<ImageVariant>> GetVariantsAsync(Guid assetId)
    {
        IReadOnlyList<ImageVariant> variants = [];
        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.AsNoTracking().SingleAsync(x => x.Id == assetId);
            variants = asset.ImageVariants.OrderBy(v => v.Width).ToList();
        });
        return variants;
    }

    private async Task<Result<ObjectStorageObjectMetadata, Error>> GetObjectMetadataAsync(string storageKey)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IObjectStorageProvider provider = scope.ServiceProvider.GetRequiredService<IObjectStorageProvider>();
        return await provider.GetMetadataAsync(storageKey, CancellationToken.None);
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
