using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using Shared.Messaging.IntegrationEvents.MaterialProcessing.Events;
using MaterialProcessingService.Contracts.AiSettings.Dtos;
using MaterialProcessingService.Contracts.AiSettings.Requests;
using MaterialProcessingService.Core.Features.Timecodes.Processing;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

/// <summary>
/// Авто-обработка видео при готовности (issue #648): консьюмер VideoReadyForProcessing
/// (фильтр usage / тогл / дедуп / атрибуция владельца) + публикация уведомления о сбое
/// только для AUTO-job'ов.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class AutoVideoProcessingTests : MaterialProcessingServiceTestsBase
{
    public AutoVideoProcessingTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    private static VideoReadyForProcessing MaterialVideoReady(
        Guid assetId,
        Guid ownerId,
        Guid? materialId = null) =>
        new(
            AssetId: assetId,
            AssetVersion: Guid.NewGuid(),
            UsageType: FileEventsRouting.UsageTypes.MATERIAL_VIDEO,
            TargetEntityId: materialId ?? Guid.NewGuid(),
            TargetEntityType: FileEventsRouting.EntityTypes.MATERIAL,
            UploadedByUserId: ownerId);

    [Fact]
    public async Task VideoReady_MaterialVideo_TogglOn_CreatesAutoJob()
    {
        Guid videoId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(MaterialVideoReady(videoId, ownerId, materialId));

        await ExecuteInDb(async db =>
        {
            TimecodeGenerationJob job = await db.TimecodeGenerationJobs.SingleAsync(x => x.VideoAssetId == videoId);
            Assert.Equal(TimecodeTriggerSource.AUTO, job.TriggerSource);
            Assert.Equal(TimecodeGenerationJobMode.TIMECODES, job.Mode);
            Assert.Equal(ownerId, job.RequestedByUserId);
            Assert.Equal(materialId, job.MaterialId);
            Assert.Equal(TimecodeGenerationStatus.Queued, job.Status);
        });

        // Pipeline-сообщение опубликовано в фоновую очередь.
        Assert.Contains(
            Factory.OutboxCollector.Messages,
            m => m is GenerateTimecodesJob);
    }

    [Fact]
    public async Task VideoReady_NonMaterialUsage_DoesNotCreateJob()
    {
        Guid videoId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new VideoReadyForProcessing(
            AssetId: videoId,
            AssetVersion: Guid.NewGuid(),
            UsageType: FileEventsRouting.UsageTypes.COURSE_VIDEO,
            TargetEntityId: Guid.NewGuid(),
            TargetEntityType: FileEventsRouting.EntityTypes.COURSE,
            UploadedByUserId: Guid.NewGuid()));

        int jobCount = await ExecuteInDb(db =>
            db.TimecodeGenerationJobs.CountAsync(x => x.VideoAssetId == videoId));
        Assert.Equal(0, jobCount);
    }

    [Fact]
    public async Task VideoReady_ToggleOff_DoesNotCreateJob()
    {
        AuthenticateAsAdmin();
        UpdateAiModelSettingsRequest request = new(
            SpeechToText: new AiModelSlotDto("openai/gpt-4o-transcribe", 0, 4000, 900),
            TimecodeGeneration: new AiModelSlotDto("openai/gpt-4.1-nano", 0.1, 6000, 300),
            ContentGeneration: new AiModelSlotDto("openai/gpt-4.1-mini", 0.2, 8000, 300),
            AutoProcessVideosEnabled: false);
        HttpResponseMessage putResponse = await AppHttpClient.PutAsJsonAsync(
            new Uri("/material-processing/admin/ai-settings/", UriKind.Relative), request);
        putResponse.EnsureSuccessStatusCode();

        Guid videoId = Guid.NewGuid();
        await InvokeMessageAndWaitAsync(MaterialVideoReady(videoId, Guid.NewGuid()));

        int jobCount = await ExecuteInDb(db =>
            db.TimecodeGenerationJobs.CountAsync(x => x.VideoAssetId == videoId));
        Assert.Equal(0, jobCount);
    }

    [Fact]
    public async Task VideoReady_Duplicate_CreatesExactlyOneJob()
    {
        Guid videoId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        VideoReadyForProcessing evt = MaterialVideoReady(videoId, ownerId);

        // Mock FileService возвращает одинаковый AssetVersion для одного videoId →
        // второе событие дедупится по (AssetId, AssetVersion).
        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int jobCount = await ExecuteInDb(db =>
            db.TimecodeGenerationJobs.CountAsync(x => x.VideoAssetId == videoId));
        Assert.Equal(1, jobCount);
    }

    [Fact]
    public async Task VideoReady_NoOwner_DoesNotCreateJob()
    {
        Guid videoId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new VideoReadyForProcessing(
            AssetId: videoId,
            AssetVersion: Guid.NewGuid(),
            UsageType: FileEventsRouting.UsageTypes.MATERIAL_VIDEO,
            TargetEntityId: Guid.NewGuid(),
            TargetEntityType: FileEventsRouting.EntityTypes.MATERIAL,
            // Owner неизвестен ни в событии, ни в source (mock по умолчанию UploadedByUserId=null).
            UploadedByUserId: null));

        int jobCount = await ExecuteInDb(db =>
            db.TimecodeGenerationJobs.CountAsync(x => x.VideoAssetId == videoId));
        Assert.Equal(0, jobCount);
    }

    [Fact]
    public async Task ManualEnqueue_CreatesJobWithManualTriggerSource()
    {
        Guid videoId = Guid.NewGuid();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/material-processing/videos/{videoId}/timecode-generations/", content: null);
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            TimecodeGenerationJob job = await db.TimecodeGenerationJobs.SingleAsync(x => x.VideoAssetId == videoId);
            Assert.Equal(TimecodeTriggerSource.MANUAL, job.TriggerSource);
            Assert.Null(job.MaterialId);
        });
    }

    [Fact]
    public async Task AutoJobFailure_PublishesVideoAutoProcessingFailed()
    {
        Guid videoId = Guid.NewGuid();
        Guid ownerId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid jobId = await SeedJobAsync(videoId, ownerId, TimecodeTriggerSource.AUTO, materialId);

        FailPipeline();
        await InvokeMessageAndWaitAsync(new GenerateTimecodesJob(jobId));

        await ExecuteInDb(async db =>
        {
            TimecodeGenerationJob job = await db.TimecodeGenerationJobs.SingleAsync(x => x.Id == jobId);
            Assert.Equal(TimecodeGenerationStatus.Failed, job.Status);
        });

        VideoAutoProcessingFailed published = Assert.Single(
            Factory.OutboxCollector.Messages.OfType<VideoAutoProcessingFailed>());
        Assert.Equal(jobId, published.JobId);
        Assert.Equal(materialId, published.MaterialId);
        Assert.Equal(videoId, published.VideoAssetId);
        Assert.Equal(ownerId, published.OwnerUserId);
    }

    [Fact]
    public async Task ManualJobFailure_DoesNotPublishNotification()
    {
        Guid videoId = Guid.NewGuid();
        Guid jobId = await SeedJobAsync(videoId, Guid.NewGuid(), TimecodeTriggerSource.MANUAL, materialId: null);

        FailPipeline();
        await InvokeMessageAndWaitAsync(new GenerateTimecodesJob(jobId));

        await ExecuteInDb(async db =>
        {
            TimecodeGenerationJob job = await db.TimecodeGenerationJobs.SingleAsync(x => x.Id == jobId);
            Assert.Equal(TimecodeGenerationStatus.Failed, job.Status);
        });

        Assert.Empty(Factory.OutboxCollector.Messages.OfType<VideoAutoProcessingFailed>());
    }

    private async Task<Guid> SeedJobAsync(
        Guid videoId,
        Guid ownerId,
        TimecodeTriggerSource triggerSource,
        Guid? materialId)
    {
        ProcessingSourceType sourceType = ProcessingSourceType.Create("HLS").Value;
        TimecodeGenerationJob job = TimecodeGenerationJob.Create(
            videoId,
            Guid.NewGuid(),
            ownerId,
            sourceType,
            TimecodeGenerationJobMode.TIMECODES,
            modelOverride: null,
            triggerSource: triggerSource,
            materialId: materialId).Value;

        await ExecuteInDb(async db =>
        {
            await db.TimecodeGenerationJobs.AddAsync(job);
            await db.SaveChangesAsync();
        });

        // Seed-вставка джоба сама по себе не должна светиться в outbox-проверках уведомления.
        Factory.OutboxCollector.Clear();
        return job.Id;
    }

    private void FailPipeline()
    {
        // Видео «без аудиодорожки» → TranscriptPreparationService возвращает ошибку,
        // job уходит в FAILED — детерминированный способ дойти до FailJobAsync.
        Factory.MediaProbe.ProbeAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<MediaProbeResult, Error>(
                new MediaProbeResult(false, TimeSpan.FromMinutes(2)))));
    }
}
