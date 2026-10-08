using System.Diagnostics.Metrics;
using Core.Database;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shared.AI;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using SharedKernel;
using SharedKernel.Exceptions;
using MaterialProcessingService.Core;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Configuration;
using MaterialProcessingService.Core.Features.ContentDrafts;
using MaterialProcessingService.Core.Features.ContentDrafts.Processing;
using MaterialProcessingService.Core.Features.Timecodes;
using MaterialProcessingService.Core.Features.Timecodes.Processing;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.IntegrationTests;

public sealed class JobHandlerReliabilityTests
{
    [Fact]
    public async Task VideoReadyHandler_ThrowsTransient_WhenProcessingSourceLookupFails()
    {
        IAiModelSettingsResolver settings = Substitute.For<IAiModelSettingsResolver>();
        settings.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new EffectiveAiModelSettings(
                new EffectiveAiModelSlot("test", "stt", 0, 4000, 300),
                new EffectiveAiModelSlot("test", "timecodes", 0, 4000, 300),
                new EffectiveAiModelSlot("test", "content", 0, 4000, 300),
                AutoProcessVideosEnabled: true,
                AiModelSettingsSource.CONFIG,
                UpdatedAt: null,
                UpdatedByUserId: null));
        IFileServiceClient files = Substitute.For<IFileServiceClient>();
        files.GetVideoProcessingSourceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<GetVideoProcessingSourceResponse?, Error>(GeneralErrors.DatabaseError()));
        IOptionsMonitor<AiPipelineFeatureFlags> featureFlags =
            Substitute.For<IOptionsMonitor<AiPipelineFeatureFlags>>();
        featureFlags.CurrentValue.Returns(new AiPipelineFeatureFlags { Enabled = true });
        var enqueuer = new TimecodeJobEnqueuer(
            Substitute.For<ITimecodeGenerationJobRepository>(),
            Substitute.For<IOutboxService>(),
            Substitute.For<ITransactionManager>());
        var handler = new VideoReadyForProcessingHandler(
            settings,
            files,
            enqueuer,
            featureFlags,
            NullLogger<VideoReadyForProcessingHandler>.Instance);
        var message = new VideoReadyForProcessing(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            FileEventsRouting.UsageTypes.MATERIAL_VIDEO,
            Guid.CreateVersion7(),
            FileEventsRouting.EntityTypes.MATERIAL,
            Guid.CreateVersion7());

        await Assert.ThrowsAsync<TransientException>(() =>
            handler.Handle(message, CancellationToken.None));
    }

    [Fact]
    public async Task TimecodeHandler_IgnoresAlreadyCompletedJob()
    {
        TimecodeGenerationJob job = CreateTimecodeJob();
        job.MarkCompleted();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();

        GenerateTimecodesJobHandler handler = CreateTimecodeHandler(job, transactions);

        await handler.Handle(new GenerateTimecodesJob(job.Id), CancellationToken.None);

        Assert.Equal(TimecodeGenerationStatus.Completed, job.Status);
        await transactions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TimecodeHandler_ThrowsTransient_WhenStartCannotBePersisted()
    {
        TimecodeGenerationJob job = CreateTimecodeJob();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        GenerateTimecodesJobHandler handler = CreateTimecodeHandler(job, transactions);

        await Assert.ThrowsAsync<TransientException>(() =>
            handler.Handle(new GenerateTimecodesJob(job.Id), CancellationToken.None));
    }

    [Fact]
    public async Task TimecodeHandler_ThrowsTransient_WhenFailureStateCannotBePersisted()
    {
        TimecodeGenerationJob job = CreateTimecodeJob();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(
                UnitResult.Success<Error>(),
                UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        GenerateTimecodesJobHandler handler = CreateTimecodeHandler(job, transactions);

        await Assert.ThrowsAsync<TransientException>(() =>
            handler.Handle(new GenerateTimecodesJob(job.Id), CancellationToken.None));
    }

    [Fact]
    public async Task TimecodeHandler_PropagatesCancellation_WithoutMarkingJobFailed()
    {
        TimecodeGenerationJob job = CreateTimecodeJob();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());
        var cancellation = new CancellationTokenSource();

        GenerateTimecodesJobHandler handler = CreateTimecodeHandler(
            job,
            transactions,
            cancellationDuringExecution: cancellation);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.Handle(new GenerateTimecodesJob(job.Id), cancellation.Token));

        Assert.Equal(TimecodeGenerationStatus.Processing, job.Status);
        Assert.Null(job.CompletedAt);
        await transactions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentHandler_ThrowsTransient_WhenStartCannotBePersisted()
    {
        ContentGenerationJob job = CreateContentJob();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        GenerateVideoContentJobHandler handler = CreateContentHandler(job, transactions);

        await Assert.ThrowsAsync<TransientException>(() =>
            handler.Handle(new GenerateVideoContentJob(job.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ContentHandler_IgnoresAlreadyCompletedJob()
    {
        ContentGenerationJob job = CreateContentJob();
        job.MarkCompleted();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();

        GenerateVideoContentJobHandler handler = CreateContentHandler(job, transactions);

        await handler.Handle(new GenerateVideoContentJob(job.Id), CancellationToken.None);

        Assert.Equal(ContentGenerationStatus.Completed, job.Status);
        await transactions.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentHandler_ThrowsTransient_WhenFailureStateCannotBePersisted()
    {
        ContentGenerationJob job = CreateContentJob();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(
                UnitResult.Success<Error>(),
                UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        GenerateVideoContentJobHandler handler = CreateContentHandler(job, transactions);

        await Assert.ThrowsAsync<TransientException>(() =>
            handler.Handle(new GenerateVideoContentJob(job.Id), CancellationToken.None));
    }

    [Fact]
    public async Task ContentHandler_PropagatesCancellation_WithoutMarkingJobFailed()
    {
        ContentGenerationJob job = CreateContentJob();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());
        var cancellation = new CancellationTokenSource();

        GenerateVideoContentJobHandler handler = CreateContentHandler(
            job,
            transactions,
            cancellationDuringExecution: cancellation);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handler.Handle(new GenerateVideoContentJob(job.Id), cancellation.Token));

        Assert.Equal(ContentGenerationStatus.Processing, job.Status);
        Assert.Null(job.CompletedAt);
        await transactions.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static GenerateTimecodesJobHandler CreateTimecodeHandler(
        TimecodeGenerationJob job,
        ITransactionManager transactions,
        CancellationTokenSource? cancellationDuringExecution = null)
    {
        ITimecodeGenerationJobRepository jobs = Substitute.For<ITimecodeGenerationJobRepository>();
        jobs.GetByAsync(Arg.Any<System.Linq.Expressions.Expression<Func<TimecodeGenerationJob, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(job);

        IFileServiceClient files = CreateFailingFileClient(cancellationDuringExecution);
        IAudioExtractor audio = Substitute.For<IAudioExtractor>();

        return new GenerateTimecodesJobHandler(
            jobs,
            audio,
            files,
            Substitute.For<IEducationContentServiceClient>(),
            CreateTranscriptPreparationService(files, audio, transactions),
            Substitute.For<ITimecodeGenerator>(),
            transactions,
            Substitute.For<IOutboxService>(),
            Options.Create(new TimecodeGenerationOptions()),
            NullLogger<GenerateTimecodesJobHandler>.Instance);
    }

    private static GenerateVideoContentJobHandler CreateContentHandler(
        ContentGenerationJob job,
        ITransactionManager transactions,
        CancellationTokenSource? cancellationDuringExecution = null)
    {
        IContentGenerationJobRepository jobs = Substitute.For<IContentGenerationJobRepository>();
        jobs.GetByAsync(Arg.Any<System.Linq.Expressions.Expression<Func<ContentGenerationJob, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(job);

        IFileServiceClient files = CreateFailingFileClient(cancellationDuringExecution);
        IAudioExtractor audio = Substitute.For<IAudioExtractor>();

        return new GenerateVideoContentJobHandler(
            jobs,
            audio,
            Substitute.For<IEducationContentServiceClient>(),
            CreateTranscriptPreparationService(files, audio, transactions),
            Substitute.For<IVideoContentGenerator>(),
            transactions,
            NullLogger<GenerateVideoContentJobHandler>.Instance);
    }

    private static TranscriptPreparationService CreateTranscriptPreparationService(
        IFileServiceClient files,
        IAudioExtractor audio,
        ITransactionManager transactions)
    {
        IVideoTranscriptRepository transcripts = Substitute.For<IVideoTranscriptRepository>();
        transcripts.GetByVideoAssetVersionAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns((MaterialProcessingService.Domain.Transcripts.VideoTranscript?)null);

        return new TranscriptPreparationService(
            transcripts,
            files,
            Substitute.For<IMediaProbe>(),
            audio,
            Substitute.For<ISpeechToTextProvider>(),
            transactions,
            Options.Create(new VideoProcessingOptions()),
            new AiPipelineMetrics(new DummyMeterFactory()),
            NullLogger<TranscriptPreparationService>.Instance);
    }

    private static IFileServiceClient CreateFailingFileClient(
        CancellationTokenSource? cancellationDuringExecution)
    {
        IFileServiceClient files = Substitute.For<IFileServiceClient>();
        files.GetVideoProcessingSourceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (cancellationDuringExecution is not null)
                    return CancelAndThrowAsync(cancellationDuringExecution);

                return Task.FromResult(Result.Failure<GetVideoProcessingSourceResponse?, Error>(
                    Error.Failure("file.source.unavailable", "Source unavailable")));
            });

        return files;
    }

    private static async Task<Result<GetVideoProcessingSourceResponse?, Error>> CancelAndThrowAsync(
        CancellationTokenSource cancellation)
    {
        await cancellation.CancelAsync();
        throw new OperationCanceledException(cancellation.Token);
    }

    private static TimecodeGenerationJob CreateTimecodeJob() =>
        TimecodeGenerationJob.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ProcessingSourceType.Create("HLS").Value).Value;

    private static ContentGenerationJob CreateContentJob() =>
        ContentGenerationJob.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            ProcessingSourceType.Create("HLS").Value).Value;

    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
