using System.Net;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using FileService.Contracts.Assets;
using NSubstitute;
using SharedKernel;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.Core.Features.Timecodes.Processing;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;
using MaterialProcessingService.Domain.Transcripts;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class TimecodeFeatureTests : MaterialProcessingServiceTestsBase
{
    public TimecodeFeatureTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GenerateTimecodes_CreatesQueuedJob_AndPublishesBackgroundMessage()
    {
        Guid videoId = Guid.CreateVersion7();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/material-processing/videos/{videoId}/timecode-generations/",
            content: null);

        response.EnsureSuccessStatusCode();

        GenerateVideoTimecodesResponse payload = await ReadResultAsync<GenerateVideoTimecodesResponse>(response);

        Assert.Equal(videoId, payload.VideoId);
        Assert.Equal("QUEUED", payload.Status);

        await ExecuteInDb(async db =>
        {
            TimecodeGenerationJob job = await db.TimecodeGenerationJobs.SingleAsync();
            Assert.Equal(payload.JobId, job.Id);
            Assert.Equal(videoId, job.VideoAssetId);
            Assert.Equal(TimecodeGenerationStatus.Queued, job.Status);
        });

        Assert.Contains(
            Factory.OutboxCollector.Messages,
            message => message is GenerateTimecodesJob queued && queued.JobId == payload.JobId);
    }

    [Fact]
    public async Task GenerateTimecodesJob_Completes_AndAppliesChaptersViaFileService()
    {
        Guid videoId = Guid.CreateVersion7();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/material-processing/videos/{videoId}/timecode-generations/",
            content: null);

        GenerateVideoTimecodesResponse payload = await ReadResultAsync<GenerateVideoTimecodesResponse>(response);

        await InvokeMessageAndWaitAsync(new GenerateTimecodesJob(payload.JobId));

        await ExecuteInDb(async db =>
        {
            TimecodeGenerationJob job = await db.TimecodeGenerationJobs.SingleAsync(x => x.Id == payload.JobId);
            Assert.Equal(TimecodeGenerationStatus.Completed, job.Status);

            VideoTranscript transcript = await db.VideoTranscripts.SingleAsync();
            Assert.Equal(videoId, transcript.VideoAssetId);
            Assert.Equal(3, transcript.Segments.Items.Count);
        });

        await Factory.FileServiceClient.Received(1).UpdateChaptersAsync(
            videoId,
            Arg.Is<UpdateVideoChaptersRequest>(request =>
                request.GenerationJobId == payload.JobId &&
                request.Chapters.Count == 2 &&
                request.Chapters[0].Title == "Введение"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTimecodesByVideo_ReturnsReadyStatusData_AfterProcessing()
    {
        Guid videoId = Guid.CreateVersion7();

        HttpResponseMessage generateResponse = await AppHttpClient.PostAsync(
            $"/material-processing/videos/{videoId}/timecode-generations/",
            content: null);

        GenerateVideoTimecodesResponse generatePayload =
            await ReadResultAsync<GenerateVideoTimecodesResponse>(generateResponse);

        await InvokeMessageAndWaitAsync(new GenerateTimecodesJob(generatePayload.JobId));

        HttpResponseMessage byVideoResponse = await AppHttpClient.GetAsync($"/material-processing/videos/{videoId}/");
        byVideoResponse.EnsureSuccessStatusCode();

        GetVideoTimecodesResponse byVideoPayload = await ReadResultAsync<GetVideoTimecodesResponse>(byVideoResponse);
        Assert.Equal(videoId, byVideoPayload.VideoId);
        Assert.True(byVideoPayload.HasTranscript);
        Assert.Null(byVideoPayload.TranscriptPreparation);
        Assert.Null(byVideoPayload.Generation);
        Assert.Null(byVideoPayload.ActiveContentGeneration);
    }

    [Fact]
    public async Task GetTimecodesByVideo_RejectsAuthorWhoDoesNotOwnVideo()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid ownerUserId = Guid.CreateVersion7();
        Guid otherAuthorId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();

        Factory.FileServiceClient
            .GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetVideoProcessingSourceResponse?, Error>(
                new GetVideoProcessingSourceResponse(
                    videoId,
                    "READY",
                    assetVersion,
                    120,
                    "HLS",
                    $"https://video.test/{videoId}/master.m3u8",
                    DateTime.UtcNow.AddMinutes(30),
                    UploadedByUserId: ownerUserId)));
        AuthenticateAs(otherAuthorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/material-processing/videos/{videoId}/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetTimecodesByVideo_ReturnsSharedTranscriptPreparation_ForActiveTimecodeJob()
    {
        Guid videoId = Guid.CreateVersion7();

        HttpResponseMessage generateResponse = await AppHttpClient.PostAsync(
            $"/material-processing/videos/{videoId}/timecode-generations/",
            content: null);

        GenerateVideoTimecodesResponse generatePayload =
            await ReadResultAsync<GenerateVideoTimecodesResponse>(generateResponse);

        HttpResponseMessage byVideoResponse = await AppHttpClient.GetAsync($"/material-processing/videos/{videoId}/");
        byVideoResponse.EnsureSuccessStatusCode();

        GetVideoTimecodesResponse byVideoPayload = await ReadResultAsync<GetVideoTimecodesResponse>(byVideoResponse);
        Assert.Equal(videoId, byVideoPayload.VideoId);
        Assert.False(byVideoPayload.HasTranscript);
        Assert.NotNull(byVideoPayload.TranscriptPreparation);
        Assert.Equal(generatePayload.JobId, byVideoPayload.TranscriptPreparation!.JobId);
        Assert.Equal("TIMECODES", byVideoPayload.TranscriptPreparation.Source);
        Assert.Equal("QUEUED", byVideoPayload.TranscriptPreparation.Status);
    }

    [Fact]
    public async Task GetTimecodesByVideo_SuppressesFailedBanner_WhenChaptersAlreadyExist()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();

        StubProcessingSource(videoId, assetVersion);
        StubChapters(videoId, [new VideoChapterDto("c1", "Введение", 0, 0)]);
        await SeedFailedTimecodeJob(videoId, assetVersion);

        GetVideoTimecodesResponse payload = await GetVideoTimecodes(videoId);

        // Артефакт (главы) уже есть → упавшая джоба не должна подниматься красным
        // баннером «Не удалось» поверх валидных глав.
        Assert.Null(payload.Generation);
    }

    [Fact]
    public async Task GetTimecodesByVideo_SurfacesFailedBanner_WhenNoChaptersExist()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();

        StubProcessingSource(videoId, assetVersion);
        StubChapters(videoId, []);
        await SeedFailedTimecodeJob(videoId, assetVersion);

        GetVideoTimecodesResponse payload = await GetVideoTimecodes(videoId);

        // Показать нечего (глав нет) → баннер о падении остаётся.
        Assert.NotNull(payload.Generation);
        Assert.Equal("FAILED", payload.Generation!.Status);
    }

    private void StubProcessingSource(Guid videoId, Guid assetVersion) =>
        Factory.FileServiceClient
            .GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                Result.Success<GetVideoProcessingSourceResponse?, Error>(
                    new GetVideoProcessingSourceResponse(
                        videoId,
                        "READY",
                        assetVersion,
                        120,
                        "HLS",
                        $"https://video.test/{videoId}/master.m3u8",
                        DateTime.UtcNow.AddMinutes(30)))));

    private void StubChapters(Guid videoId, IReadOnlyList<VideoChapterDto> chapters) =>
        Factory.FileServiceClient
            .GetVideoChaptersAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                Result.Success<GetVideoChaptersResponse?, Error>(
                    new GetVideoChaptersResponse(videoId, chapters))));

    private async Task SeedFailedTimecodeJob(Guid videoId, Guid assetVersion) =>
        await ExecuteInDb(async db =>
        {
            TimecodeGenerationJob job = TimecodeGenerationJob.Create(
                videoId,
                assetVersion,
                Guid.CreateVersion7(),
                ProcessingSourceType.Create("HLS").Value,
                TimecodeGenerationJobMode.TIMECODES,
                triggerSource: TimecodeTriggerSource.AUTO).Value;
            job.Start();
            job.MarkFailed(Error.Failure(
                "timecodes.generation.invalid",
                "Не удалось разобрать ответ генерации тайм-кодов"));

            db.TimecodeGenerationJobs.Add(job);
            await db.SaveChangesAsync();
        });

    private async Task<GetVideoTimecodesResponse> GetVideoTimecodes(Guid videoId)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/material-processing/videos/{videoId}/");
        response.EnsureSuccessStatusCode();
        return await ReadResultAsync<GetVideoTimecodesResponse>(response);
    }
}
