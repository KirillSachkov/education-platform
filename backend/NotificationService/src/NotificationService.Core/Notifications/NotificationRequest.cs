using System.Text.Json;
using NotificationService.Core.Templates;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Notifications;

/// <summary>
/// Запрос на доставку уведомления / Notification delivery request.
///
/// Handler'ы создают <see cref="NotificationRequest"/> через фабрику <see cref="From"/>, которая
/// подхватывает <c>Type</c>, <c>TemplateId</c> и каналы по умолчанию из самого шаблона —
/// в коде хендлера остаётся указать только получателя, args, payload и (опц.) correlationId.
///
/// <paramref name="RequestedChannels"/> — верхняя граница; реальный набор каналов = пересечение
/// с <c>UserNotificationChannels</c> получателя. Канал InApp всегда форсится (продуктовое решение).
/// </summary>
public sealed record NotificationRequest(
    Guid RecipientUserId,
    NotificationType Type,
    NotificationTemplate Template,
    TemplateArgs Args,
    NotificationChannel RequestedChannels,
    string PayloadJson,
    Guid? CorrelationId)
{
    /// <summary>
    /// Удобная фабрика: <c>Type</c>, <c>TemplateId</c>, <c>RequestedChannels</c> подхватываются из <paramref name="template"/>.
    /// </summary>
    /// <param name="template">Описание шаблона (catalog).</param>
    /// <param name="recipientUserId">Получатель.</param>
    /// <param name="args">Аргументы для плейсхолдеров — см. <see cref="TemplateArgs.Of"/>.</param>
    /// <param name="payload">Объект для сериализации в JSON (null → "{}"). Попадает в <c>Notification.Payload</c>
    /// и используется фронтом для роутинга (тип + payload → URL).</param>
    /// <param name="correlationId">Для идемпотентности — стабильный id исходного события
    /// (обычно user/submission/material id). <c>null</c> = без защиты от дубликатов.</param>
    /// <param name="channelsOverride">Переопределение каналов (редко; обычно берём default шаблона).</param>
    public static NotificationRequest From(
        NotificationTemplate template,
        Guid recipientUserId,
        TemplateArgs? args = null,
        object? payload = null,
        Guid? correlationId = null,
        NotificationChannel? channelsOverride = null)
    {
        string payloadJson = payload is null
            ? "{}"
            : JsonSerializer.Serialize(payload);

        return new NotificationRequest(
            RecipientUserId: recipientUserId,
            Type: template.Type,
            Template: template,
            Args: args ?? TemplateArgs.Empty,
            RequestedChannels: channelsOverride ?? template.DefaultChannels,
            PayloadJson: payloadJson,
            CorrelationId: correlationId);
    }
}
