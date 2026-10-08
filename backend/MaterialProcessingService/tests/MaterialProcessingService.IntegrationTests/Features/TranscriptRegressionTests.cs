using System.Net;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;
using MaterialProcessingService.Contracts.Timecodes.Dtos;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class TranscriptRegressionTests : MaterialProcessingServiceTestsBase
{
    public TranscriptRegressionTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetByVideo_DoesNotFail_WhenPersistedTranscriptRowContainsInvalidSegmentsJson()
    {
        Guid videoId = Guid.CreateVersion7();
        const string brokenSegmentsJson = "[\"broken\"]";

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
                    {Guid.CreateVersion7()},
                    120,
                    {"ru"},
                    cast({brokenSegmentsJson} as jsonb),
                    timezone('utc', now()),
                    timezone('utc', now())
                )
                """);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/material-processing/videos/{videoId}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetVideoTimecodesResponse payload = await ReadResultAsync<GetVideoTimecodesResponse>(response);
        Assert.Equal(videoId, payload.VideoId);
        Assert.False(payload.HasTranscript);
        Assert.Null(payload.TranscriptPreparation);
        Assert.Null(payload.Generation);
        Assert.Null(payload.ActiveContentGeneration);
    }

    [Fact]
    public async Task GetByVideo_DoesNotReportTranscriptReady_WhenOnlyStaleAssetVersionTranscriptExists()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid staleAssetVersion = Guid.CreateVersion7();
        Guid currentAssetVersion = Guid.CreateVersion7();
        const string validSegmentsJson =
            """
            [
              { "startSeconds": 0, "endSeconds": 10, "text": "Старый transcript" }
            ]
            """;

        Factory.FileServiceClient.GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<GetVideoProcessingSourceResponse?, SharedKernel.Error>(
                new GetVideoProcessingSourceResponse(
                    videoId,
                    "READY",
                    currentAssetVersion,
                    120,
                    "HLS",
                    $"https://video.test/{videoId}/master.m3u8",
                    DateTime.UtcNow.AddMinutes(30))));

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
                    {staleAssetVersion},
                    120,
                    {"ru"},
                    cast({validSegmentsJson} as jsonb),
                    timezone('utc', now()),
                    timezone('utc', now())
                )
                """);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/material-processing/videos/{videoId}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetVideoTimecodesResponse payload = await ReadResultAsync<GetVideoTimecodesResponse>(response);
        Assert.Equal(videoId, payload.VideoId);
        Assert.Equal(currentAssetVersion, payload.AssetVersion);
        Assert.False(payload.HasTranscript);
        Assert.Null(payload.TranscriptPreparation);
    }

    [Fact]
    public async Task GetByVideo_UsesLatestCompletedContentJobVersion_WhenFileServiceIsUnavailable()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid materialId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();
        Guid requestedByUserId = Guid.CreateVersion7();
        const string validSegmentsJson =
            """
            [
              { "startSeconds": 0, "endSeconds": 12, "text": "Актуальный transcript" }
            ]
            """;

        Factory.FileServiceClient.GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GetVideoProcessingSourceResponse?, SharedKernel.Error>(GeneralErrors.DatabaseError()));

        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                insert into material_processing.content_generation_jobs (
                    id,
                    video_asset_id,
                    material_id,
                    asset_version,
                    requested_by_user_id,
                    status,
                    stage,
                    progress_percent,
                    source_type,
                    error_code,
                    error_message,
                    created_at,
                    started_at,
                    completed_at,
                    updated_at
                )
                values (
                    {Guid.CreateVersion7()},
                    {videoId},
                    {materialId},
                    {assetVersion},
                    {requestedByUserId},
                    {"COMPLETED"},
                    {"SAVE"},
                    100,
                    {"HLS"},
                    null,
                    null,
                    timezone('utc', now()),
                    timezone('utc', now()),
                    timezone('utc', now()),
                    timezone('utc', now())
                )
                """);

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
                    cast({validSegmentsJson} as jsonb),
                    timezone('utc', now()),
                    timezone('utc', now())
                )
                """);

            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/material-processing/videos/{videoId}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetVideoTimecodesResponse payload = await ReadResultAsync<GetVideoTimecodesResponse>(response);
        Assert.Equal(videoId, payload.VideoId);
        Assert.Equal(assetVersion, payload.AssetVersion);
        Assert.True(payload.HasTranscript);
        Assert.Null(payload.TranscriptPreparation);
    }
}
