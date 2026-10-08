using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Materials;
using Microsoft.Extensions.Logging;
using SharedKernel;
using Core.Database;
using MaterialProcessingService.Core.Media;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Domain.ContentDrafts;
using SharedKernel.Exceptions;

namespace MaterialProcessingService.Core.Features.ContentDrafts.Processing;

public sealed class GenerateVideoContentJobHandler
{
    private readonly IContentGenerationJobRepository _jobRepository;
    private readonly IAudioExtractor _audioExtractor;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly TranscriptPreparationService _transcriptPreparationService;
    private readonly IVideoContentGenerator _contentGenerator;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<GenerateVideoContentJobHandler> _logger;

    public GenerateVideoContentJobHandler(
        IContentGenerationJobRepository jobRepository,
        IAudioExtractor audioExtractor,
        IEducationContentServiceClient educationContentServiceClient,
        TranscriptPreparationService transcriptPreparationService,
        IVideoContentGenerator contentGenerator,
        ITransactionManager transactionManager,
        ILogger<GenerateVideoContentJobHandler> logger)
    {
        _jobRepository = jobRepository;
        _audioExtractor = audioExtractor;
        _educationContentServiceClient = educationContentServiceClient;
        _transcriptPreparationService = transcriptPreparationService;
        _contentGenerator = contentGenerator;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(GenerateVideoContentJob message, CancellationToken cancellationToken)
    {
        ContentGenerationJob? job = await _jobRepository.GetByAsync(
            x => x.Id == message.JobId,
            cancellationToken);

        if (job is null)
        {
            _logger.LogWarning("Content job {JobId} not found", message.JobId);
            return;
        }

        try
        {
            if (job.Status is ContentGenerationStatus.Completed or ContentGenerationStatus.Failed)
                return;

            UnitResult<Error> startResult = await StartJobAsync(job, cancellationToken);
            if (startResult.IsFailure)
                throw startResult.Error.ToException();

            UnitResult<Error> executionResult;
            try
            {
                executionResult = await ExecuteAsync(job, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TransientException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during content generation job {JobId}", job.Id);
                executionResult = UnitResult.Failure<Error>(
                    Error.Failure("content.generation_failed", "Не удалось сгенерировать контент"));
            }

            if (executionResult.IsSuccess)
                return;

            if (executionResult.Error.Behavior == ErrorBehavior.Transient)
                throw executionResult.Error.ToException();

            UnitResult<Error> failResult = await FailJobAsync(
                job,
                executionResult.Error,
                CancellationToken.None);
            if (failResult.IsFailure)
                throw failResult.Error.ToException();
        }
        finally
        {
            await _audioExtractor.CleanupAsync(job.Id, CancellationToken.None);
        }
    }

    private async Task<UnitResult<Error>> ExecuteAsync(
        ContentGenerationJob job,
        CancellationToken cancellationToken)
    {
        // ContentGenerationJob.ModelOverride интерпретируется как content-LLM-модель.
        // STT использует default из конфига (если транскрипт ещё не закэширован).
        Result<TranscriptPreparationResult, Error> preparationResult =
            await _transcriptPreparationService.PrepareAsync(
                new TranscriptPreparationRequest(
                    job.VideoAssetId,
                    job.AssetVersion,
                    job.Id,
                    "content.transcript.empty"),
                async (stage, progress, token) =>
                    // Сбой записи прогресс-стадии не роняет pipeline — discard осознанный.
                    _ = await UpdateStageAsync(
                        job,
                        MapTranscriptStage(stage),
                        progress,
                        token),
                cancellationToken);

        if (preparationResult.IsFailure)
            return preparationResult.Error;

        UnitResult<Error> updateGenerateStageResult =
            await UpdateStageAsync(job, ContentGenerationStage.Generate, 85, cancellationToken);
        if (updateGenerateStageResult.IsFailure)
            return updateGenerateStageResult.Error;

        Result<GeneratedVideoContentResult, Error> generatedResult = await _contentGenerator.GenerateAsync(
            preparationResult.Value.Transcript,
            preparationResult.Value.VideoDuration,
            job.ModelOverride,
            cancellationToken);
        if (generatedResult.IsFailure)
            return generatedResult.Error;

        UnitResult<Error> updateSaveStageResult =
            await UpdateStageAsync(job, ContentGenerationStage.Save, 95, cancellationToken);
        if (updateSaveStageResult.IsFailure)
            return updateSaveStageResult.Error;

        UnitResult<Error> applyResult = await _educationContentServiceClient.UpdateMaterialContentAsync(
            job.MaterialId,
            new UpdateMaterialContentRequest(
                job.Id,
                job.VideoAssetId,
                job.AssetVersion,
                generatedResult.Value.ContentMarkdown),
            cancellationToken);
        if (applyResult.IsFailure)
            return applyResult.Error;

        return await CompleteJobAsync(job, cancellationToken);
    }

    private async Task<UnitResult<Error>> StartJobAsync(
        ContentGenerationJob job,
        CancellationToken cancellationToken)
    {
        job.Start();
        return await SaveChangesAsync(cancellationToken);
    }

    private async Task<UnitResult<Error>> UpdateStageAsync(
        ContentGenerationJob job,
        ContentGenerationStage stage,
        int progressPercent,
        CancellationToken cancellationToken)
    {
        job.UpdateStage(stage, progressPercent);
        return await SaveChangesAsync(cancellationToken);
    }

    private async Task<UnitResult<Error>> CompleteJobAsync(
        ContentGenerationJob job,
        CancellationToken cancellationToken)
    {
        job.MarkCompleted();
        return await SaveChangesAsync(cancellationToken);
    }

    private async Task<UnitResult<Error>> FailJobAsync(
        ContentGenerationJob job,
        Error error,
        CancellationToken cancellationToken)
    {
        job.MarkFailed(error);

        CancellationToken persistCancellationToken = cancellationToken.IsCancellationRequested
            ? CancellationToken.None
            : cancellationToken;

        return await SaveChangesAsync(persistCancellationToken);
    }

    private async Task<UnitResult<Error>> SaveChangesAsync(CancellationToken cancellationToken)
    {
        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to persist content job state: {Error}",
                saveResult.Error.GetMessage());
        }

        return saveResult.IsFailure
            ? UnitResult.Failure<Error>(saveResult.Error.AsTransient())
            : saveResult;
    }

    private static ContentGenerationStage MapTranscriptStage(TranscriptPreparationStage stage) =>
        stage switch
        {
            TranscriptPreparationStage.Probe => ContentGenerationStage.Probe,
            TranscriptPreparationStage.AudioExtract => ContentGenerationStage.AudioExtract,
            TranscriptPreparationStage.Transcribe => ContentGenerationStage.Transcribe,
            _ => ContentGenerationStage.SourceFetch
        };
}
