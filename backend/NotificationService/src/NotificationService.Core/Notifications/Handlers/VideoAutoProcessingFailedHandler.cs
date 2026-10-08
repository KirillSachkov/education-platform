using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.MaterialProcessing.Events;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>material_processing.events / video.auto_processing.failed</c> → владельцу видео (#648).
///
/// Платформа сама запустила транскрипцию + тайм-коды по готовности видео, но pipeline упал
/// (например, STT-провайдер вернул ошибку). Автор кнопку не нажимал и не видит статус на
/// странице — сигналим ему in-app + Telegram с дип-линком в редактор материала, где можно
/// перезапустить вручную. Ручные (MANUAL) job'ы это событие не публикуют. Correlation = JobId
/// (идемпотентность к Wolverine-retry; перезалив видео → новый job → новое уведомление).
/// Название материала резолвится через ECS (cached), на failure — graceful fallback.
/// </summary>
public sealed class VideoAutoProcessingFailedHandler
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly ILogger<VideoAutoProcessingFailedHandler> _logger;

    public VideoAutoProcessingFailedHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        ILogger<VideoAutoProcessingFailedHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _logger = logger;
    }

    public async Task Handle(VideoAutoProcessingFailed evt, CancellationToken ct)
    {
        string materialTitle = await ResolveMaterialTitleAsync(evt.MaterialId, ct);
        string reason = string.IsNullOrWhiteSpace(evt.ErrorMessage)
            ? "ошибка обработки"
            : evt.ErrorMessage;

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.VideoAutoProcessingFailed,
            recipientUserId: evt.OwnerUserId,
            correlationId: evt.JobId,
            args: TemplateArgs.Of(
                ("materialTitle", materialTitle),
                ("reason", reason)),
            payload: new { materialId = evt.MaterialId, videoAssetId = evt.VideoAssetId });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private async Task<string> ResolveMaterialTitleAsync(Guid materialId, CancellationToken ct)
    {
        Result<MaterialSearchLookupDto, Error> lookup = await _ecsClient.GetMaterialSearchLookupAsync(materialId, ct);
        if (lookup.IsSuccess && lookup.Value is not null && !string.IsNullOrWhiteSpace(lookup.Value.Title))
            return lookup.Value.Title;

        _logger.LogWarning(
            "ECS lookup failed for material {MaterialId}: {Error}. Using fallback.",
            materialId, lookup.ErrorText());
        return "видео";
    }
}
