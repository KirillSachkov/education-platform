using CSharpFunctionalExtensions;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Configuration;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Core.Features.Timecodes.Processing;

/// <summary>
///     Реактивно запускает транскрипцию + тайм-коды, когда FileService сообщил,
///     что material-видео готово (<c>VideoReadyForProcessing</c>, exchange
///     <c>file.events</c>, ключ <c>file.ready.material</c>). Issue #648.
///
///     Гейты (в порядке): только <c>material_video</c> → глобальный AI-kill-switch →
///     глобальный тогл авто-обработки → дедуп по <c>(AssetId, AssetVersion)</c>
///     (идемпотентность к re-delivery). Job создаётся с <c>TriggerSource=AUTO</c> и
///     <c>RequestedByUserId = UploadedByUserId</c> видео; при падении такого job'а
///     публикуется уведомление владельцу (см. <see cref="GenerateTimecodesJobHandler"/>).
///
///     Временные ошибки FileService/БД пробрасываются как transient, чтобы durable
///     Wolverine inbox повторил событие. Некорректные данные события пропускаются.
/// </summary>
public sealed class VideoReadyForProcessingHandler
{
    private readonly IAiModelSettingsResolver _settingsResolver;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly TimecodeJobEnqueuer _enqueuer;
    private readonly IOptionsMonitor<AiPipelineFeatureFlags> _featureFlags;
    private readonly ILogger<VideoReadyForProcessingHandler> _logger;

    public VideoReadyForProcessingHandler(
        IAiModelSettingsResolver settingsResolver,
        IFileServiceClient fileServiceClient,
        TimecodeJobEnqueuer enqueuer,
        IOptionsMonitor<AiPipelineFeatureFlags> featureFlags,
        ILogger<VideoReadyForProcessingHandler> logger)
    {
        _settingsResolver = settingsResolver;
        _fileServiceClient = fileServiceClient;
        _enqueuer = enqueuer;
        _featureFlags = featureFlags;
        _logger = logger;
    }

    public async Task Handle(VideoReadyForProcessing message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.UsageType, FileEventsRouting.UsageTypes.MATERIAL_VIDEO, StringComparison.Ordinal))
            return;

        if (!_featureFlags.CurrentValue.Enabled)
        {
            _logger.LogInformation(
                "Auto-processing skipped for video {VideoId}: AI pipeline globally disabled",
                message.AssetId);
            return;
        }

        EffectiveAiModelSettings settings = await _settingsResolver.GetAsync(cancellationToken);
        if (!settings.AutoProcessVideosEnabled)
        {
            _logger.LogInformation(
                "Auto-processing skipped for video {VideoId}: AutoProcessVideosEnabled toggle is off",
                message.AssetId);
            return;
        }

        Result<GetVideoProcessingSourceResponse?, Error> sourceResult =
            await _fileServiceClient.GetVideoProcessingSourceAsync(message.AssetId, cancellationToken);
        if (sourceResult.IsFailure)
        {
            _logger.LogWarning(
                "Auto-processing source lookup failed for video {VideoId}: {Error}; message will be retried",
                message.AssetId,
                sourceResult.Error.Type);
            throw sourceResult.Error.AsTransient().ToException();
        }

        if (sourceResult.Value is null)
        {
            _logger.LogInformation(
                "Auto-processing skipped for video {VideoId}: processing source not found (not a video asset?)",
                message.AssetId);
            return;
        }

        Guid ownerUserId = (message.UploadedByUserId ?? sourceResult.Value.UploadedByUserId) ?? Guid.Empty;
        if (ownerUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "Auto-processing skipped for video {VideoId}: no UploadedByUserId to attribute the job to",
                message.AssetId);
            return;
        }

        Result<ProcessingSourceType, Error> sourceTypeResult =
            ProcessingSourceType.Create(sourceResult.Value.SourceType);
        if (sourceTypeResult.IsFailure)
        {
            _logger.LogWarning(
                "Auto-processing skipped for video {VideoId}: invalid source type '{SourceType}'",
                message.AssetId,
                sourceResult.Value.SourceType);
            return;
        }

        Result<TimecodeEnqueueResult, Error> enqueueResult = await _enqueuer.EnqueueAsync(
            message.AssetId,
            sourceResult.Value.AssetVersion,
            sourceTypeResult.Value,
            ownerUserId,
            modelOverride: null,
            TimecodeTriggerSource.AUTO,
            materialId: message.TargetEntityId,
            dedupByVersion: true,
            cancellationToken);

        if (enqueueResult.IsFailure)
        {
            _logger.LogWarning(
                "Auto-processing enqueue failed for video {VideoId}: {Error}",
                message.AssetId,
                enqueueResult.Error.GetMessage());

            if (enqueueResult.Error.Type == ErrorType.FAILURE)
                throw enqueueResult.Error.AsTransient().ToException();

            return;
        }

        _logger.LogInformation(
            "Auto-processing for video {VideoId} (material {MaterialId}): {Outcome} (job {JobId})",
            message.AssetId,
            message.TargetEntityId,
            enqueueResult.Value.Outcome,
            enqueueResult.Value.JobId);
    }
}
