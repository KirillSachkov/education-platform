namespace NotificationService.Core.Templates;

/// <summary>
/// Отрендеренное сообщение для конкретного канала / Rendered message for a specific channel.
/// <paramref name="Title"/> — заголовок (для email это Subject письма, для InApp — заголовок
/// в колокольчике; Telegram игнорирует и склеивает в текст).
/// <paramref name="Body"/> — тело: plain-text для InApp, MarkdownV1 для Telegram, HTML для email.
/// <paramref name="PlainTextBody"/> — используется только email-каналом для multipart/alternative.
/// </summary>
public sealed record RenderedMessage(string Title, string Body, string? PlainTextBody = null);
