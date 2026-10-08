namespace Shared.Messaging.IntegrationEvents.Files.Events;

/// <summary>
///     Опубликовано FileService, когда видео-asset достиг состояния "готов к AI-обработке":
///     Kinescope закончил кодирование (статус <c>done</c> → <c>AssetStatus.READY</c>) И asset
///     привязан к целевой сущности. В отличие от <see cref="FileAssetBound"/> (летит и при bind'е
///     ещё кодирующегося видео), это событие — чистый сигнал "видео готово", на который
///     MaterialProcessingService вешает авто-запуск транскрипции + тайм-кодов. Issue #648.
/// </summary>
public sealed record VideoReadyForProcessing(
    Guid AssetId,
    Guid AssetVersion,
    string UsageType,
    Guid TargetEntityId,
    string TargetEntityType,
    Guid? UploadedByUserId);
