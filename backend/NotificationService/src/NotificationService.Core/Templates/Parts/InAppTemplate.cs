namespace NotificationService.Core.Templates.Parts;

/// <summary>
/// Канал-специфичная часть шаблона для сайта (inbox, drawer, колокольчик) /
/// Channel-specific template part for the in-app inbox.
///
/// Минималистичный плоский текст без markdown/html. <see cref="Title"/> используется как
/// заголовок карточки, <see cref="Body"/> — как короткое описание под ним.
/// Плейсхолдеры <c>{name}</c> подставляются из <see cref="TemplateArgs"/>.
/// </summary>
public sealed record InAppTemplate(string Title, string Body);
