using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Files;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CancelFileEdgeCaseTests : FileServiceTestsBase
{
    public CancelFileEdgeCaseTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task CancelFileUpload_ReadyNonTemporaryFile_ReturnsBadRequest()
    {
        InitiateFileUploadResponse initiateResult = await InitiateAndCompleteFileAsync();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/files/{initiateResult.AssetId}/cancel", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.READY, asset.Status);
        });
    }

    [Fact]
    public async Task CancelFileUpload_NonExistentFile_ReturnsNotFound()
    {
        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/files/{Guid.NewGuid()}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelFileUpload_FailedFile_MarksAsDeleted()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(CreateCoursePreviewRequest());

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.Equal(HttpStatusCode.NotFound, completeResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.FAILED, asset.Status);
        });

        HttpResponseMessage cancelResponse = await AppHttpClient.PostAsync(
            $"/files/{initiateResult.AssetId}/cancel", null);

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            MediaAsset asset = await db.MediaAssets.SingleAsync(x => x.Id == initiateResult.AssetId);
            Assert.Equal(AssetStatus.DELETED, asset.Status);
        });
    }

    [Fact]
    public async Task CompleteFileUpload_AlreadyCompletedFile_ReturnsIdempotentResponse()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(
            CreateCoursePreviewRequest(size: 4));
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage firstComplete = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        Assert.Equal(HttpStatusCode.OK, firstComplete.StatusCode);

        HttpResponseMessage secondComplete = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));

        Assert.True(
            secondComplete.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest,
            $"Expected OK (idempotent) or BadRequest, got {secondComplete.StatusCode}");
    }

    private async Task<InitiateFileUploadResponse> InitiateAndCompleteFileAsync()
    {
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(
            CreateCoursePreviewRequest(size: 4));
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
}
