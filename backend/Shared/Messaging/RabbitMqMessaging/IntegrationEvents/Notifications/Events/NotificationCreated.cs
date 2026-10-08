namespace Shared.Messaging.IntegrationEvents.Notifications.Events;

/// <summary>
/// Published when a notification is created in the inbox.
/// Consumers: SSE fan-out (NotificationService in-process), TelegramBotService (delivers via bot).
/// PayloadJson contains backend-resolved targetUrl for click routing.
/// </summary>
/// <param name="Channels">Bitmask of requested channels (InApp=1, Telegram=2, Email=4).</param>
/// <param name="Title">InApp-рендер заголовка / InApp title render (minimalist plain text).</param>
/// <param name="Body">InApp-рендер тела / InApp body render (plain text, for inbox/drawer).</param>
/// <param name="TelegramBody">
/// Готовый текст для Telegram (MarkdownV1, emoji, escape'нутые user-values).
/// Не-null только если канал Telegram включён в <paramref name="Channels"/> — иначе NotificationService
/// не тратит ресурсы на рендер.
/// </param>
public sealed record NotificationCreated(
    Guid NotificationId,
    Guid RecipientUserId,
    short Type,
    short Channels,
    string TemplateId,
    string Title,
    string Body,
    string? TelegramBody,
    string PayloadJson,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt);
