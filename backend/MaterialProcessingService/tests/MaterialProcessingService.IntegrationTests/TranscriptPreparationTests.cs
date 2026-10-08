using Core.Database;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shared.AI;
using SharedKernel;
using MaterialProcessingService.Core;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Transcripts;
using MaterialProcessingService.Domain.Transcripts.ValueObjects;

namespace MaterialProcessingService.IntegrationTests;

public sealed class TranscriptPreparationTests
{
    [Fact]
    public async Task PrepareAsync_UsesCachedTranscript_WithoutExternalProcessing()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();

        TranscriptDuration duration = TranscriptDuration.Create(TimeSpan.FromSeconds(12)).Value;
        Language language = Language.Create("ru").Value;
        VideoTranscriptSegment firstSegment = VideoTranscriptSegment.Create(
            TranscriptSegmentRange.Create(0, 4).Value,
            TranscriptSegmentText.Create("Первый фрагмент").Value).Value;
        VideoTranscriptSegment secondSegment = VideoTranscriptSegment.Create(
            TranscriptSegmentRange.Create(4, 8).Value,
            TranscriptSegmentText.Create("Второй фрагмент").Value).Value;
        TranscriptSegments segments = TranscriptSegments.Create([firstSegment, secondSegment]).Value;

        VideoTranscript cachedTranscript = VideoTranscript.Create(
            videoId,
            assetVersion,
            duration,
            language,
            segments).Value;

        IVideoTranscriptRepository transcriptRepository = Substitute.For<IVideoTranscriptRepository>();
        transcriptRepository
            .GetByVideoAssetVersionAsync(videoId, assetVersion, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(cachedTranscript);

        IFileServiceClient fileServiceClient = Substitute.For<IFileServiceClient>();
        IMediaProbe mediaProbe = Substitute.For<IMediaProbe>();
        IAudioExtractor audioExtractor = Substitute.For<IAudioExtractor>();
        ISpeechToTextProvider speechToTextProvider = Substitute.For<ISpeechToTextProvider>();
        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();

        var service = new TranscriptPreparationService(
            transcriptRepository,
            fileServiceClient,
            mediaProbe,
            audioExtractor,
            speechToTextProvider,
            transactionManager,
            Options.Create(new VideoProcessingOptions()),
            new AiPipelineMetrics(new DummyMeterFactory()),
            NullLogger<TranscriptPreparationService>.Instance);

        Result<TranscriptPreparationResult, Error> result = await service.PrepareAsync(
            new TranscriptPreparationRequest(
                videoId,
                assetVersion,
                Guid.CreateVersion7(),
                "video.transcript.empty"),
            static (_, _, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(12), result.Value.VideoDuration);
        Assert.Equal(2, result.Value.Transcript.Segments.Count);

        await fileServiceClient
            .DidNotReceive()
            .GetVideoProcessingSourceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await mediaProbe
            .DidNotReceive()
            .ProbeAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<CancellationToken>());

        await audioExtractor
            .DidNotReceive()
            .ExtractChunksAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await speechToTextProvider
            .DidNotReceive()
            .TranscribeAsync(Arg.Any<AudioChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PrepareAsync_ReusesTranscriptPersistedConcurrently_WhenSaveFails()
    {
        Guid videoId = Guid.CreateVersion7();
        Guid assetVersion = Guid.CreateVersion7();

        TranscriptDuration duration = TranscriptDuration.Create(TimeSpan.FromSeconds(120)).Value;
        Language language = Language.Create("ru").Value;
        VideoTranscriptSegment persistedSegment = VideoTranscriptSegment.Create(
            TranscriptSegmentRange.Create(0, 10).Value,
            TranscriptSegmentText.Create("Сохраненный фрагмент").Value).Value;
        TranscriptSegments persistedSegments = TranscriptSegments.Create([persistedSegment]).Value;

        VideoTranscript persistedTranscript = VideoTranscript.Create(
            videoId,
            assetVersion,
            duration,
            language,
            persistedSegments).Value;

        int transcriptLookupCount = 0;
        IVideoTranscriptRepository transcriptRepository = Substitute.For<IVideoTranscriptRepository>();
        transcriptRepository
            .GetByVideoAssetVersionAsync(videoId, assetVersion, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                transcriptLookupCount++;
                return transcriptLookupCount == 1
                    ? null
                    : persistedTranscript;
            });

        IFileServiceClient fileServiceClient = Substitute.For<IFileServiceClient>();
        fileServiceClient.GetVideoProcessingSourceAsync(videoId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<GetVideoProcessingSourceResponse?, Error>(
                new GetVideoProcessingSourceResponse(
                    videoId,
                    "READY",
                    assetVersion,
                    120,
                    "HLS",
                    $"https://video.test/{videoId}/master.m3u8",
                    DateTime.UtcNow.AddMinutes(30)))));

        IMediaProbe mediaProbe = Substitute.For<IMediaProbe>();
        mediaProbe.ProbeAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<MediaProbeResult, Error>(
                new MediaProbeResult(true, TimeSpan.FromSeconds(120)))));

        IAudioExtractor audioExtractor = Substitute.For<IAudioExtractor>();
        audioExtractor.ExtractChunksAsync(Arg.Any<VideoProcessingSource>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<IReadOnlyList<AudioChunk>, Error>(
                [new AudioChunk("chunk.mp3", TimeSpan.Zero, TimeSpan.FromSeconds(120))])));

        ISpeechToTextProvider speechToTextProvider = Substitute.For<ISpeechToTextProvider>();
        speechToTextProvider.TranscribeAsync(Arg.Any<AudioChunk>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success<SpeechToTextResult, Error>(
                new SpeechToTextResult(
                    "ru",
                    [new TranscriptSegment(TimeSpan.Zero, TimeSpan.FromSeconds(20), "Новый фрагмент")],
                    SpeechTimestampSource.Model))));

        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        var service = new TranscriptPreparationService(
            transcriptRepository,
            fileServiceClient,
            mediaProbe,
            audioExtractor,
            speechToTextProvider,
            transactionManager,
            Options.Create(new VideoProcessingOptions()),
            new AiPipelineMetrics(new DummyMeterFactory()),
            NullLogger<TranscriptPreparationService>.Instance);

        Result<TranscriptPreparationResult, Error> result = await service.PrepareAsync(
            new TranscriptPreparationRequest(
                videoId,
                assetVersion,
                Guid.CreateVersion7(),
                "video.transcript.empty"),
            static (_, _, _) => Task.CompletedTask,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(120), result.Value.VideoDuration);
        Assert.Single(result.Value.Transcript.Segments);
        Assert.Equal("Сохраненный фрагмент", result.Value.Transcript.Segments[0].Text);

        await transcriptRepository.Received(1).AddAsync(Arg.Any<VideoTranscript>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///     Минимальный <see cref="IMeterFactory"/> для unit-тестов — production OTel-pipeline
    ///     не нужен, метрики просто стираются.
    /// </summary>
    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
