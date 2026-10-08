using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Domain;
using FileService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;

namespace FileService.IntegrationTests.Features.Files;

/// <summary>
///     L2 outbox tests: проверяют что endpoint реально публикует событие через
///     IOutboxService (TestOutboxCollector ловит публикации; внешний RabbitMQ
///     заглушен через DisableAllExternalWolverineTransports, persistence — через
///     DisableAllWolverineMessagePersistence).
///
///     Если handler случайно потеряет SaveChangesAsync перед return —
///     OutboxCollector останется пустым и .Single() бросит.
///     HTTP-status тесты в FileFlowTests этого не поймали бы.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class FileIntegrationEventsTests : FileServiceTestsBase
{
    public FileIntegrationEventsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CompleteFileUpload_ShouldPublishFileAssetBound()
    {
        InitiateFileUploadRequest initiateRequest = CreateCoursePreviewRequest(size: 4);
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(initiateRequest);
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetBound published = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("file", published.Kind);
        Assert.Equal("course_preview", published.UsageType);
        Assert.Equal(initiateRequest.TargetEntity!.Id, published.TargetEntityId);
        Assert.Equal(initiateRequest.TargetEntity.Type, published.TargetEntityType);
        Assert.False(published.RequiresAuthoritativeConfirmation);
    }

    [Fact]
    public async Task CompleteFileUpload_OnSlotReplacement_WaitsForAggregateConfirmationBeforeDeletingPrevious()
    {
        // Arrange: первый CoursePreview закреплён за курсом и стал READY.
        Guid courseId = Guid.NewGuid();
        InitiateFileUploadRequest firstRequest = CreateCoursePreviewRequest(courseId, size: 4);
        InitiateFileUploadResponse firstResult = await InitiateFileUploadAsync(firstRequest);
        await UploadToDirectUrlAsync(firstResult.UploadUrl, firstResult.RequiredHeaders, [1, 2, 3, 4]);
        HttpResponseMessage firstComplete = await AppHttpClient.PostAsJsonAsync(
            $"/files/{firstResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        firstComplete.EnsureSuccessStatusCode();
        long firstRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == firstResult.AssetId)).BindingRevision);
        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(firstResult.AssetId, firstRevision));

        // Второй preview под тот же courseId — slot-replacement должен снести первый.
        InitiateFileUploadRequest secondRequest = CreateCoursePreviewRequest(courseId, size: 5);
        InitiateFileUploadResponse secondResult = await InitiateFileUploadAsync(secondRequest);
        await UploadToDirectUrlAsync(secondResult.UploadUrl, secondResult.RequiredHeaders, [9, 8, 7, 6, 5]);

        // Act: complete второго — выпадет один FileAssetBound (новый) +
        // domain-effect RequestDelete на старом. FileAssetDeleted для старого
        // публикует AssetRetentionService в отдельном фоне, поэтому в рамках
        // этой транзакции мы видим только bound. Что важно — этот единственный
        // bound должен быть про второй ассет, а не про первый.
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/files/{secondResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetBound bound = OutboxCollector.OfType<FileAssetBound>().Single();
        Assert.Equal(secondResult.AssetId, bound.AssetId);
        Assert.NotEqual(firstResult.AssetId, bound.AssetId);

        long secondRevision = await ExecuteInDb(async db =>
            (await db.MediaAssets.SingleAsync(asset => asset.Id == secondResult.AssetId)).BindingRevision);

        await ExecuteInDb(async db =>
        {
            Assert.Equal(
                AssetStatus.READY,
                (await db.MediaAssets.SingleAsync(asset => asset.Id == firstResult.AssetId)).Status);
        });

        await InvokeMessageAndWaitAsync(new FileAssetBindingConfirmed(secondResult.AssetId, secondRevision));
        await InvokeMessageAndWaitAsync(new FileAssetDetached(firstResult.AssetId, firstRevision));

        // Only a revision-matched confirmation may retire the predecessor.
        await ExecuteInDb(async db =>
        {
            MediaAsset first = await db.MediaAssets.SingleAsync(a => a.Id == firstResult.AssetId);
            MediaAsset second = await db.MediaAssets.SingleAsync(a => a.Id == secondResult.AssetId);
            Assert.Equal(AssetStatus.DELETING, first.Status);
            Assert.Equal(AssetStatus.READY, second.Status);
        });
    }

    [Fact]
    public async Task DeleteFile_ShouldPublishFileAssetDeleted()
    {
        InitiateFileUploadRequest initiateRequest = CreateCoursePreviewRequest(size: 4);
        InitiateFileUploadResponse initiateResult = await InitiateFileUploadAsync(initiateRequest);
        await UploadToDirectUrlAsync(initiateResult.UploadUrl, initiateResult.RequiredHeaders, [1, 2, 3, 4]);

        HttpResponseMessage completeResponse = await AppHttpClient.PostAsJsonAsync(
            $"/files/{initiateResult.AssetId}/complete",
            new CompleteFileUploadRequest(null));
        completeResponse.EnsureSuccessStatusCode();

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/files/{initiateResult.AssetId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        FileAssetDeleted published = OutboxCollector.OfType<FileAssetDeleted>().Single();
        Assert.Equal(initiateResult.AssetId, published.AssetId);
        Assert.Equal("file", published.Kind);
        Assert.Equal("course_preview", published.UsageType);
        Assert.Equal(initiateRequest.TargetEntity!.Id, published.TargetEntityId);
        Assert.Equal(initiateRequest.TargetEntity.Type, published.TargetEntityType);
    }

    private async Task<InitiateFileUploadResponse> InitiateFileUploadAsync(InitiateFileUploadRequest request)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/files/uploads", request);
        response.EnsureSuccessStatusCode();

        Envelope<InitiateFileUploadResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InitiateFileUploadResponse>>();

        return envelope!.Result!;
    }

    private static async Task UploadToDirectUrlAsync(
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

    private static InitiateFileUploadRequest CreateCoursePreviewRequest(long size = 4) =>
        CreateCoursePreviewRequest(Guid.NewGuid(), size);

    private static InitiateFileUploadRequest CreateCoursePreviewRequest(Guid courseId, long size = 4) =>
        new(
            FileName: "preview.png",
            ContentType: "image/png",
            Size: size,
            UsageType: "course_preview",
            DraftId: null,
            TargetEntity: new TargetEntityDto("course", courseId));
}
