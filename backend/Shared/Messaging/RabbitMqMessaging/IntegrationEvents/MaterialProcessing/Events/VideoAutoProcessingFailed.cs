namespace Shared.Messaging.IntegrationEvents.MaterialProcessing.Events;

/// <summary>
///     Опубликовано MaterialProcessingService, когда АВТО-запущенный (не ручной) job обработки
///     видео упал. NotificationService слушает это событие и шлёт владельцу видео in-app
///     уведомление с дип-линком на редактор материала. Ручные job'ы это событие не публикуют —
///     автор сам нажал кнопку и видит статус на странице. Issue #648.
/// </summary>
public sealed record VideoAutoProcessingFailed(
    Guid JobId,
    Guid MaterialId,
    Guid VideoAssetId,
    Guid OwnerUserId,
    string ErrorCode,
    string ErrorMessage);
