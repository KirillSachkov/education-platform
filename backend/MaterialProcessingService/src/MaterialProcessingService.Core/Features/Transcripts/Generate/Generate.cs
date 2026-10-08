using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel;
using MaterialProcessingService.Contracts.Transcripts.Dtos;
using MaterialProcessingService.Core.Configuration;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.Features.Timecodes.Processing;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Core.Features.Transcripts.Generate;

/// <summary>
///     Готовит только транскрипцию видео — без последующей генерации тайм-кодов или
///     конспекта. После завершения автор может отдельно запустить timecode-generations
///     или content-drafts; <c>TranscriptPreparationService</c> переиспользует уже готовый
///     транскрипт по AssetVersion, поэтому повторно тяжёлый STT не запускается.
///
///     Реализация переиспользует <see cref="TimecodeGenerationJob"/> с
///     <see cref="TimecodeGenerationJobMode.TRANSCRIPT_ONLY"/> — handler делает early-exit
///     после стадии Save транскрипта.
/// </summary>
public sealed record GenerateVideoTranscriptCommand(
    Guid VideoId,
    string? ModelOverride = null,
    bool Force = false,
    /// <summary>
    ///     Admin-only override для requestedByUserId — нужен, когда client_credentials токен
    ///     (mcp-admin) имеет sub=client_id и UserId=Guid.Empty. Игнорируется не-admin'ом.
    /// </summary>
    Guid? RequestedBy = null) : ICommand;

public sealed class GenerateVideoTranscriptValidator : AbstractValidator<GenerateVideoTranscriptCommand>
{
    public GenerateVideoTranscriptValidator()
    {
        RuleFor(x => x.VideoId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GenerateVideoTranscriptCommand.VideoId)));

        RuleFor(x => x.ModelOverride)
            .MaximumLength(AiModelSlot.MAX_MODEL_LENGTH)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GenerateVideoTranscriptCommand.ModelOverride)));
    }
}

public sealed class GenerateVideoTranscriptEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/material-processing/videos/{videoId:guid}/transcripts/", async Task<EndpointResult<GenerateVideoTranscriptResponse>> (
                [FromRoute] Guid videoId,
                [FromQuery(Name = "modelOverride")] string? modelOverride,
                // Default false — minimal API считает non-nullable bool обязательным,
                // отсутствие `?force=` в query даёт 400 на binding-стадии. Frontend
                // не пересылает параметр когда false → нужен явный default.
                [FromQuery(Name = "force")] bool force = false,
                [FromQuery(Name = "requestedBy")] Guid? requestedBy = null,
                [FromServices] ICommandHandler<GenerateVideoTranscriptResponse, GenerateVideoTranscriptCommand> handler = default!,
                CancellationToken cancellationToken = default) =>
                await handler.Handle(new GenerateVideoTranscriptCommand(videoId, modelOverride, force, requestedBy), cancellationToken))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE)
            .RequireRateLimiting("ai-generation");
    }
}

public sealed class GenerateVideoTranscriptHandler
    : ICommandHandler<GenerateVideoTranscriptResponse, GenerateVideoTranscriptCommand>
{
    private readonly ITimecodeGenerationJobRepository _jobRepository;
    private readonly IVideoTranscriptRepository _transcriptRepository;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IOptionsMonitor<AiPipelineFeatureFlags> _featureFlags;
    private readonly IValidator<GenerateVideoTranscriptCommand> _validator;

    public GenerateVideoTranscriptHandler(
        ITimecodeGenerationJobRepository jobRepository,
        IVideoTranscriptRepository transcriptRepository,
        IFileServiceClient fileServiceClient,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        UserScopedData user,
        IOptionsMonitor<AiPipelineFeatureFlags> featureFlags,
        IValidator<GenerateVideoTranscriptCommand> validator)
    {
        _jobRepository = jobRepository;
        _transcriptRepository = transcriptRepository;
        _fileServiceClient = fileServiceClient;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _user = user;
        _featureFlags = featureFlags;
        _validator = validator;
    }

    public async Task<Result<GenerateVideoTranscriptResponse, Error>> Handle(
        GenerateVideoTranscriptCommand command,
        CancellationToken cancellationToken)
    {
        if (!_featureFlags.CurrentValue.Enabled)
            return Error.Failure("ai.pipeline.disabled", "AI-обработка временно отключена администратором");

        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        TimecodeGenerationJob? activeJob = await _jobRepository.GetActiveByVideoAsync(
            command.VideoId,
            cancellationToken);

        if (activeJob is not null)
            return new GenerateVideoTranscriptResponse(
                activeJob.Id,
                command.VideoId,
                activeJob.Status.ToStorageValue());

        Result<GetVideoProcessingSourceResponse?, Error> sourceResult =
            await _fileServiceClient.GetVideoProcessingSourceAsync(command.VideoId, cancellationToken);

        if (sourceResult.IsFailure)
            return sourceResult.Error;

        if (sourceResult.Value is null)
            return GeneralErrors.NotFound(command.VideoId);

        // Ownership: только владелец видео (или admin) может запускать STT.
        // P0 BOLA: без этого checkа любой platform-author мог тратить чужой
        // AI-бюджет на чужих видео — STT-only стоит ~1000 ₽ за 60-min ролик
        // на gpt-4o-audio-preview (audio-LLM, дорогой режим).
        UnitResult<Error> ownership = _user.CheckOwnership(sourceResult.Value.UploadedByUserId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<ProcessingSourceType, Error> sourceTypeResult =
            ProcessingSourceType.Create(sourceResult.Value.SourceType);
        if (sourceTypeResult.IsFailure)
            return sourceTypeResult.Error;

        // ModelOverride accept'им только от caller'а с Ai.OVERRIDE_MODEL permission —
        // обычные авторы передают, но получают default из appsettings/DB-override.
        string? modelOverride = _user.HasPermission(PlatformPermissions.Ai.OVERRIDE_MODEL)
            ? command.ModelOverride
            : null;

        // Service-token (client_credentials) имеет sub=client_id → UserId=Guid.Empty.
        // Bulk-runner передаёт явный requestedBy. Caller'ы без Ai.IMPERSONATE_REQUESTED_BY
        // permission используют свой UserId.
        Guid requestedByUserId = _user.UserId != Guid.Empty
            ? _user.UserId
            : (_user.HasPermission(PlatformPermissions.Ai.IMPERSONATE_REQUESTED_BY)
                && command.RequestedBy is { } adminBy
                    ? adminBy
                    : Guid.Empty);

        Result<TimecodeGenerationJob, Error> jobResult = TimecodeGenerationJob.Create(
            command.VideoId,
            sourceResult.Value.AssetVersion,
            requestedByUserId,
            sourceTypeResult.Value,
            TimecodeGenerationJobMode.TRANSCRIPT_ONLY,
            modelOverride: modelOverride);
        if (jobResult.IsFailure)
            return jobResult.Error;

        TimecodeGenerationJob job = jobResult.Value;

        // Force-regeneration в одной транзакции с созданием job'а: либо обе
        // операции (delete cached transcript + add new job) фиксируются, либо
        // ни одна. Без транзакции крах после delete оставлял бы пользователя
        // без кэша И без новой job'ы — pipeline залипал бы до очередной
        // перезаливки видео (новая AssetVersion).
        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        if (command.Force)
        {
            await _transcriptRepository.DeleteByVideoAssetVersionAsync(
                command.VideoId,
                sourceResult.Value.AssetVersion,
                cancellationToken);
        }

        await _jobRepository.AddAsync(job, cancellationToken);
        await _outboxService.PublishAsync(new GenerateTimecodesJob(job.Id));

        UnitResult<Error> saveResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            TimecodeGenerationJob? concurrentJob = await _jobRepository.GetActiveByVideoAsync(
                command.VideoId,
                cancellationToken);

            if (concurrentJob is not null)
                return new GenerateVideoTranscriptResponse(
                    concurrentJob.Id,
                    command.VideoId,
                    concurrentJob.Status.ToStorageValue());

            return saveResult.Error;
        }

        return new GenerateVideoTranscriptResponse(
            job.Id,
            command.VideoId,
            job.Status.ToStorageValue());
    }
}
