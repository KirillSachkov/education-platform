using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Templates.Rendering;

/// <summary>
/// Стратегия рендеринга шаблона под конкретный канал / Strategy to render a template for a specific channel.
///
/// Одна реализация на канал. InAppRenderer — plain text; TelegramRenderer — MarkdownV1 + escape
/// пользовательских значений; EmailRenderer — HTML с общим layout'ом + plain-text fallback.
/// </summary>
public interface IChannelRenderer
{
    /// <summary>
    /// Канал, за который отвечает renderer. «Единичный» флаг, не комбинация.
    /// </summary>
    NotificationChannel Channel { get; }

    /// <summary>
    /// Рендерит шаблон с аргументами / Renders the template with the given arguments.
    /// </summary>
    RenderedMessage Render(NotificationTemplate template, TemplateArgs args);
}
