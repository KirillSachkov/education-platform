using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using EducationContentService.Contracts.Materials;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Contracts.ContentDrafts.Dtos;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Features.ContentDrafts.Processing;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ContentDraftFeatureTests : MaterialProcessingServiceTestsBase
{
    public ContentDraftFeatureTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GenerateContent_CreatesQueuedJob_AndPublishesBackgroundMessage()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/material-processing/videos/{videoId}/content-generations/",
            new GenerateVideoContentRequest(materialId));

        response.EnsureSuccessStatusCode();

        GenerateVideoContentResponse payload = await ReadResultAsync<GenerateVideoContentResponse>(response);

        Assert.Equal(videoId, payload.VideoId);
        Assert.Equal(materialId, payload.MaterialId);
        Assert.Equal("QUEUED", payload.Status);

        await ExecuteInDb(async db =>
        {
            ContentGenerationJob job = await db.ContentGenerationJobs.SingleAsync();
            Assert.Equal(payload.JobId, job.Id);
            Assert.Equal(videoId, job.VideoAssetId);
            Assert.Equal(materialId, job.MaterialId);
            Assert.Equal(ContentGenerationStatus.Queued, job.Status);
        });

        Assert.Contains(
            Factory.OutboxCollector.Messages,
            message => message is GenerateVideoContentJob queued && queued.JobId == payload.JobId);
    }

    [Fact]
    public async Task GenerateContentJob_Completes_AndAppliesContentViaEducationService()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/material-processing/videos/{videoId}/content-generations/",
            new GenerateVideoContentRequest(materialId));

        GenerateVideoContentResponse payload = await ReadResultAsync<GenerateVideoContentResponse>(response);

        await InvokeMessageAndWaitAsync(new GenerateVideoContentJob(payload.JobId));

        await ExecuteInDb(async db =>
        {
            ContentGenerationJob job = await db.ContentGenerationJobs.SingleAsync(x => x.Id == payload.JobId);
            Assert.Equal(ContentGenerationStatus.Completed, job.Status);
        });

        await Factory.EducationContentServiceClient.Received(1).UpdateMaterialContentAsync(
            materialId,
            Arg.Is<UpdateMaterialContentRequest>(request =>
                request.GenerationJobId == payload.JobId &&
                request.VideoId == videoId &&
                request.Markdown.Contains("Конспект")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTimecodesByVideo_ReturnsSharedTranscriptPreparation_ForActiveContentGeneration()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/material-processing/videos/{videoId}/content-generations/",
            new GenerateVideoContentRequest(materialId));

        GenerateVideoContentResponse payload = await ReadResultAsync<GenerateVideoContentResponse>(response);

        HttpResponseMessage byVideoResponse = await AppHttpClient.GetAsync($"/material-processing/videos/{videoId}/");
        byVideoResponse.EnsureSuccessStatusCode();

        GetVideoTimecodesResponse byVideoPayload = await ReadResultAsync<GetVideoTimecodesResponse>(byVideoResponse);
        Assert.Equal(videoId, byVideoPayload.VideoId);
        Assert.False(byVideoPayload.HasTranscript);
        Assert.NotNull(byVideoPayload.TranscriptPreparation);
        Assert.Equal(payload.JobId, byVideoPayload.TranscriptPreparation!.JobId);
        Assert.Equal("CONTENT", byVideoPayload.TranscriptPreparation.Source);
        Assert.Equal(materialId, byVideoPayload.TranscriptPreparation.MaterialId);
        Assert.NotNull(byVideoPayload.ActiveContentGeneration);
        Assert.Equal(materialId, byVideoPayload.ActiveContentGeneration!.MaterialId);
        Assert.Equal("QUEUED", byVideoPayload.ActiveContentGeneration.Status);
    }

    [Fact]
    public async Task GenerateContentJob_UsesPersistedCamelCaseTranscript_WithoutRepeatedAiRequests()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();
        const string segmentsJson =
            """
            [
              { "startSeconds": 0, "endSeconds": 12, "text": "Первый фрагмент" },
              { "startSeconds": 12, "endSeconds": 24, "text": "Второй фрагмент" }
            ]
            """;

        Factory.FileServiceClient.GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                Result.Success<GetVideoProcessingSourceResponse?, SharedKernel.Error>(
                    new GetVideoProcessingSourceResponse(
                        videoId,
                        "READY",
                        assetVersion,
                        120,
                        "HLS",
                        $"https://video.test/{videoId}/master.m3u8",
                        DateTime.UtcNow.AddMinutes(30)))));

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into material_processing.video_transcripts (
                    id,
                    video_asset_id,
                    asset_version,
                    duration_seconds,
                    language,
                    segments_json,
                    created_at,
                    updated_at
                )
                values (
                    {Guid.CreateVersion7()},
                    {videoId},
                    {assetVersion},
                    120,
                    {"ru"},
                    cast({segmentsJson} as jsonb),
                    timezone('utc', now()),
                    timezone('utc', now())
                )
                """);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/material-processing/videos/{videoId}/content-generations/",
            new GenerateVideoContentRequest(materialId));

        GenerateVideoContentResponse payload = await ReadResultAsync<GenerateVideoContentResponse>(response);

        await InvokeMessageAndWaitAsync(new GenerateVideoContentJob(payload.JobId));

        await ExecuteInDb(async db =>
        {
            ContentGenerationJob job = await db.ContentGenerationJobs.SingleAsync(x => x.Id == payload.JobId);
            Assert.Equal(ContentGenerationStatus.Completed, job.Status);
        });

        await Factory.FileServiceClient.Received(1)
            .GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>());

        await Factory.MediaProbe.DidNotReceive()
            .ProbeAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<CancellationToken>());

        await Factory.AudioExtractor.DidNotReceive()
            .ExtractChunksAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await Factory.SpeechToTextProvider.DidNotReceive()
            .TranscribeAsync(Arg.Any<AudioChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        await Factory.EducationContentServiceClient.Received(1).UpdateMaterialContentAsync(
            materialId,
            Arg.Is<UpdateMaterialContentRequest>(request =>
                request.GenerationJobId == payload.JobId &&
                request.VideoId == videoId &&
                request.Markdown.Contains("Конспект")),
            Arg.Any<CancellationToken>());
    }
}
