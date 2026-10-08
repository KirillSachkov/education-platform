namespace NotificationService.Core.Templates.Parts;

/// <summary>
/// Канал-специфичная часть шаблона для Telegram / Channel-specific template part for Telegram.
///
/// Формат: MarkdownV1 + emoji. У Telegram-сообщения нет «отдельного заголовка» — всё в едином
/// тексте, поэтому хранится только <see cref="Body"/>. Если нужен жирный заголовок — обернуть
/// в <c>*звёздочки*</c> внутри Body.
///
/// В значениях args символы <c>_ * ` [ ] ( )</c> экранируются автоматически
/// (<see cref="Rendering.TelegramRenderer"/>) — сами тексты в Body не экранируются.
/// </summary>
public sealed record TelegramTemplate(string Body);
