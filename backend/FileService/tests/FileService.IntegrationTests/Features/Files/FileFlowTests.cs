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

namespace FileService.IntegrationTests.Features.Files;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class FileFlowTests : FileServiceTestsBase
{
    public FileFlowTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task InitiateFileUpload_CreatesPendingAsset_AndReturnsUploadSession()
    {
        InitiateFileUploadRequest request = CreateCoursePreviewRequest();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<InitiateFileUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        Assert.NotNull(envelope?.Result);
        Assert.Equal("pending_upload", envelope.Result.Status);
        Assert.NotEmpty(envelope.Result.UploadUrl);
        Assert.Equal($"/api/files/{envelope.Result.AssetId}/content", envelope.Result.ContentUrl);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == envelope.Result.AssetId);
            FileStorageRef storageRef = await db.FileStorageRefs.SingleAsync(x => x.AssetId == envelope.Result.AssetId);

            Assert.Equal(AssetStatus.PENDING_UPLOAD, asset.Status);
            Assert.Equal(AssetUsageType.COURSE_PREVIEW, asset.UsageType);
            Assert.Equal($"files/{envelope.Result.AssetId:N}.png", storageRef.StorageKey.Value);
        });
    }

    [Fact]
    public async Task DirectUpload_LargerThanDeclaredSize_IsRejectedByStorage()
    {
        InitiateFileUploadResponse initiated =
            await InitiateFileUploadAsync(CreateCoursePreviewRequest(size: 4));
        using HttpClient client = new();
        using HttpRequestMessage request = new(HttpMethod.Put, initiated.UploadUrl)
        {
            Content = new ByteArrayContent([1, 2, 3, 4, 5]),
        };
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task CompleteFileUpload_AfterDirectUpload_MarksAssetReady_AndReturnsRedirectContentUrl()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(CreateCoursePreviewRequest(size: 4));
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        Envelope<CompleteFileUploadResponse>? completeEnvelope =
            await completeResponse.Content.ReadFromJsonAsync<Envelope<CompleteFileUploadResponse>>();

        Assert.NotNull(completeEnvelope?.Result);
        Assert.Equal("ready", completeEnvelope.Result.Status);

        HttpClient noRedirectClient = CreateAppClient(allowAutoRedirect: false);
        HttpResponseMessage contentResponse =
            await noRedirectClient.GetAsync($"/files/{initiateResult.AssetId}/content");

        Assert.Equal(HttpStatusCode.Redirect, contentResponse.StatusCode);
        Assert.NotNull(contentResponse.Headers.Location);

        // course_preview — inline-ресурс: presigned URL прокидывает Content-Type
        // через response-content-type, но НЕ ставит attachment-disposition,
        // иначе Firefox/Safari блокируют картинки через OpaqueResponseBlocking
        // на cross-origin redirect (см. #183).
        string location = contentResponse.Headers.Location!.ToString();
        Assert.Contains("response-content-type=", location, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response-content-disposition=", location, StringComparison.OrdinalIgnoreCase);

        // Cache-Control: public preview → long public cache + immutable
        // (#359 image performance — warm pages reuse the 302 instead of
        // hitting FileService on every <img> render).
        string cacheControl = string.Join(',', contentResponse.Headers.CacheControl?.ToString() ?? string.Empty);
        Assert.Contains("public", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("immutable", cacheControl, StringComparison.OrdinalIgnoreCase);

        HttpResponseMessage fileResponse = await AppHttpClient.GetAsync($"/files/{initiateResult.AssetId}");
        Envelope<GetFileResponse?>? fileEnvelope =
            await fileResponse.Content.ReadFromJsonAsync<Envelope<GetFileResponse?>>();

        Assert.NotNull(fileEnvelope?.Result);
        Assert.Equal("ready", fileEnvelope.Result.Status);
        Assert.Equal("course_preview", fileEnvelope.Result.UsageType);
        Assert.Equal($"/api/files/{initiateResult.AssetId}/content", fileEnvelope.Result.ContentUrl);
    }

    [Fact]
    public async Task GetFileContent_ForMarkdownFile_ReturnsRedirectWithAttachmentDisposition()
    {
        // markdown_file — это вложение, поэтому presigned URL должен прошить
        // attachment-disposition с original filename (RFC 6266), чтобы браузер
        // при скачивании показал реальное имя, а не storage GUID (см. #176).
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(CreateMarkdownFileRequest(size: 4));
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        completeResponse.EnsureSuccessStatusCode();

        HttpClient noRedirectClient = CreateAppClient(allowAutoRedirect: false);
        HttpResponseMessage contentResponse =
            await noRedirectClient.GetAsync($"/files/{initiateResult.AssetId}/content");

        Assert.Equal(HttpStatusCode.Redirect, contentResponse.StatusCode);
        Assert.NotNull(contentResponse.Headers.Location);

        string location = contentResponse.Headers.Location!.ToString();
        Assert.Contains("response-content-type=", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("response-content-disposition=", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attachment", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filename", location, StringComparison.OrdinalIgnoreCase);

        // Cache-Control: protected (markdown_file in material/issue) → short
        // private cache so entitlement revocation propagates within ~1 min
        // (#359 image performance).
        string cacheControl = contentResponse.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("private", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("immutable", cacheControl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteFileUpload_ReturnsNotFound_WhenObjectWasNotUploaded()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(CreateCoursePreviewRequest());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.FAILED, asset.Status);
        });
    }

    [Fact]
    public async Task CompleteFileUpload_ReturnsBadRequest_WhenUploadedSizeMismatches()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(CreateCoursePreviewRequest(size: 4));
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE media_assets SET size = 8 WHERE id = {initiateResult.AssetId}");
        });

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.FAILED, asset.Status);
        });
    }

    [Fact]
    public async Task CancelFileUpload_MarksPendingFileAsDeleted()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(CreateCoursePreviewRequest());

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/files/{initiateResult.AssetId}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    [Fact]
    public async Task DeleteFile_MarksReadyFileDeleting_AndPublishesDeletedEvent()
    {
        InitiateFileUploadResponse initiateResult = await InitiateAndCompleteFileAsync();

        // Phase 1: handler transitions to DELETING and publishes the outbox event.
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/files/{initiateResult.AssetId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetDeleted published = OutboxCollector.OfType<FileAssetDeleted>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("file", published.Kind);
        Assert.Equal("course_preview", published.UsageType);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETING, asset.Status);
        });

        // Phase 2: retention sweep performs the S3 delete and finalizes the status.
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AssetRetentionService retention = scope.ServiceProvider.GetRequiredService<AssetRetentionService>();
        int processed = await retention.ProcessDeletingAssetsAsync(CancellationToken.None);
        Assert.Equal(1, processed);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    private async Task<InitiateFileUploadResponse> InitiateAndCompleteFileAsync()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(CreateCoursePreviewRequest(size: 4));
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
        using HttpRequestMessage request = new(HttpMethod.Put, uploadUrl) { Content = new ByteArrayContent(content), };

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

    private InitiateFileUploadRequest CreateCoursePreviewRequest(long size = 4) =>
        new(
            FileName: "preview.png",
            ContentType: "image/png",
            Size: size,
            UsageType: "course_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("course", Guid.NewGuid()));

    private InitiateFileUploadRequest CreateMarkdownFileRequest(long size = 4) =>
        new(
            FileName: "report.pdf",
            ContentType: "application/pdf",
            Size: size,
            UsageType: "markdown_file",
            DraftId: null,
            TargetEntity: new TargetEntityDto("material", Guid.NewGuid()));
}
