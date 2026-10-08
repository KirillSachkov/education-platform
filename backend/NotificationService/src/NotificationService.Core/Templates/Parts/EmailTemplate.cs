namespace NotificationService.Core.Templates.Parts;

/// <summary>
/// Канал-специфичная часть шаблона для email / Channel-specific template part for email.
///
/// <see cref="Subject"/> — почтовый Subject (длиннее и более «почтовый», чем InApp-заголовок).
/// <see cref="HtmlBodyResource"/> — имя <c>.html</c> файла из <c>Templates/Emails/</c>
/// (embedded resource), оборачивается в общий <c>_layout.html</c>. HTML-escape применяется
/// к значениям args автоматически (<see cref="Rendering.EmailRenderer"/>).
/// <see cref="PlainText"/> — fallback для почтовых клиентов без HTML; если <c>null</c>,
/// рендер берёт <see cref="InAppTemplate.Body"/>.
/// </summary>
public sealed record EmailTemplate(
    string Subject,
    string HtmlBodyResource,
    string? PlainText = null);
