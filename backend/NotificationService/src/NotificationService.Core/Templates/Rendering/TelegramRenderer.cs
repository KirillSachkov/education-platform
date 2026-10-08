using System.Text;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Templates.Rendering;

/// <summary>
/// Рендер Telegram-уведомления / Renders the Telegram notification.
///
/// Формат: MarkdownV1 + emoji. Шаблон в <see cref="Parts.TelegramTemplate.Body"/> — наш текст
/// с контролируемой разметкой; в <b>значениях</b> args символы Markdown'а
/// (<c>_ * ` [ ] ( )</c>) экранируются автоматически, чтобы автор/студент не сломали разметку
/// именем курса вида <c>_test_</c> или комментарием с инлайн-ссылкой <c>[click](url)</c>.
///
/// Если у шаблона нет <see cref="Parts.TelegramTemplate"/>, renderer бросит — это сигнал
/// о баге диспатча (он должен был отфильтровать канал через <c>NotificationTemplate.Supports</c>).
/// </summary>
public sealed class TelegramRenderer : IChannelRenderer
{
    public NotificationChannel Channel => NotificationChannel.Telegram;

    public RenderedMessage Render(NotificationTemplate template, TemplateArgs args)
    {
        if (template.Telegram is null)
            throw new InvalidOperationException(
                $"Template '{template.Id}' has no Telegram part — dispatcher should have filtered this channel.");

        string body = TemplateRenderer.Substitute(template.Telegram.Body, args, EscapeMarkdownV1);
        // Title в Telegram-сообщении не нужен (Telegram не показывает «subject»),
        // но сохраняем рендер InApp-title для логов и delivery audit.
        string title = TemplateRenderer.Substitute(template.InApp.Title, args, EscapeMarkdownV1);
        return new RenderedMessage(title, body);
    }

    /// <summary>
    /// Экранирует символы MarkdownV1 в значениях args / Escapes MarkdownV1 chars in arg values.
    /// Telegram MarkdownV1: <c>_*`[]()</c> используются для bold/italic/code/inline-link.
    /// Если значение содержит <c>[text](url)</c>, без экранирования его можно интерпретировать
    /// как inline-ссылку — потенциальный injection vector.
    /// </summary>
    internal static string EscapeMarkdownV1(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        StringBuilder sb = new(value.Length);
        foreach (char ch in value)
        {
            if (ch is '_' or '*' or '`' or '[' or ']' or '(' or ')')
                sb.Append('\\');
            sb.Append(ch);
        }
        return sb.ToString();
    }
}
