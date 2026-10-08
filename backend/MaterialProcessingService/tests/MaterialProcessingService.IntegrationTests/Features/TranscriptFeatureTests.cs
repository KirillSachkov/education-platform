using System.Net;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using NSubstitute;
using SharedKernel;
using MaterialProcessingService.Contracts.Transcripts.Dtos;
using MaterialProcessingService.Core.Features.Timecodes.Processing;
using MaterialProcessingService.Domain.Timecodes;
using MaterialProcessingService.IntegrationTests.Infrastructure;

namespace MaterialProcessingService.IntegrationTests.Features;

public sealed class TranscriptFeatureTests : MaterialProcessingServiceTestsBase
{
    public TranscriptFeatureTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GenerateTranscript_CompletesWithoutTimecodes_AndSrtIsOwnerProtected()
    {
        Guid ownerUserId = Guid.CreateVersion7();
        Guid otherAuthorId = Guid.CreateVersion7();
        Guid videoId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();
        StubProcessingSource(videoId, assetVersion, ownerUserId);
        AuthenticateAs(ownerUserId, "platform-author");

        HttpResponseMessage enqueueResponse = await AppHttpClient.PostAsync(
            $"/material-processing/videos/{videoId}/transcripts/",
            content: null);
        enqueueResponse.EnsureSuccessStatusCode();
        GenerateVideoTranscriptResponse payload =
            await ReadResultAsync<GenerateVideoTranscriptResponse>(enqueueResponse);

        await InvokeMessageAndWaitAsync(new GenerateTimecodesJob(payload.JobId));

        await ExecuteInDb(async dbContext =>
        {
            TimecodeGenerationJob job = await dbContext.TimecodeGenerationJobs.FindAsync(payload.JobId)
                ?? throw new InvalidOperationException("Transcript job was not persisted");
            Assert.Equal(TimecodeGenerationJobMode.TRANSCRIPT_ONLY, job.Mode);
            Assert.Equal(TimecodeGenerationStatus.Completed, job.Status);
            Assert.Single(dbContext.VideoTranscripts);
        });
        await Factory.TimecodeGenerator.DidNotReceive().GenerateAsync(
            Arg.Any<MaterialProcessingService.Core.Transcripts.Transcript>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());

        HttpResponseMessage ownerResponse = await AppHttpClient.GetAsync(
            $"/material-processing/videos/{videoId}/subtitles.srt");
        ownerResponse.EnsureSuccessStatusCode();
        Assert.Equal("application/x-subrip", ownerResponse.Content.Headers.ContentType?.MediaType);
        string srt = await ownerResponse.Content.ReadAsStringAsync();
        Assert.Contains("Введение в тему", srt, StringComparison.Ordinal);

        AuthenticateAs(otherAuthorId, "platform-author");
        HttpResponseMessage otherResponse = await AppHttpClient.GetAsync(
            $"/material-processing/videos/{videoId}/subtitles.srt");
        Assert.Equal(HttpStatusCode.Forbidden, otherResponse.StatusCode);
    }

    [Fact]
    public async Task GetSrt_ReturnsNotFound_WhenTranscriptIsMissing()
    {
        Guid ownerUserId = Guid.CreateVersion7();
        Guid videoId = Guid.CreateVersion7();
        StubProcessingSource(videoId, Guid.CreateVersion7(), ownerUserId);
        AuthenticateAs(ownerUserId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/material-processing/videos/{videoId}/subtitles.srt");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private void StubProcessingSource(Guid videoId, Guid assetVersion, Guid ownerUserId) =>
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
}
