using System.Net;
using NotificationService.Core.Templates.Parts;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Templates.Rendering;

/// <summary>
/// Рендер email-уведомления / Renders the email notification.
///
/// Алгоритм:
/// <list type="number">
/// <item><c>Subject</c> = <see cref="EmailTemplate.Subject"/> с подстановкой args.</item>
/// <item>Inner HTML — embedded resource <see cref="EmailTemplate.HtmlBodyResource"/> +
/// HTML-escape значений args.</item>
/// <item>Inner HTML вставляется в <c>_layout.html</c> через sentinel-плейсхолдер
/// (см. <see cref="BODY_SENTINEL"/>). Layout-уровень args (например <c>{frontendUrl}</c>) — это
/// конфиг (не user-input), HTML-escape для них не применяется, иначе ссылки с query-string
/// поломаются.</item>
/// <item>Plain-text — <see cref="EmailTemplate.PlainText"/> ?? <see cref="InAppTemplate.Body"/>.</item>
/// </list>
///
/// Бросает <see cref="InvalidOperationException"/>, если у шаблона нет email-части — это
/// сигнал о баге диспатча.
/// </summary>
public sealed class EmailRenderer : IChannelRenderer
{
    private const string LAYOUT_RESOURCE = "_layout.html";

    /// <summary>
    /// Sentinel для слота тела письма в layout. Не использует <c>{...}</c>-синтаксис, чтобы не
    /// конфликтовать с TemplateRenderer'ом (тот мог бы съесть слот, если бы handler передал
    /// arg с ключом <c>"body"</c>).
    /// </summary>
    private const string BODY_SENTINEL = "@@CONTENT_BODY@@";
    private const string LAYOUT_BODY_PLACEHOLDER = "{body}";

    public NotificationChannel Channel => NotificationChannel.Email;

    public RenderedMessage Render(NotificationTemplate template, TemplateArgs args)
    {
        if (template.Email is null)
            throw new InvalidOperationException(
                $"Template '{template.Id}' has no Email part — dispatcher should have filtered this channel.");

        EmailTemplate email = template.Email;

        string subject = TemplateRenderer.Substitute(email.Subject, args);
        string innerHtml = RenderInnerHtml(email, args);
        string fullHtml = WrapInLayout(innerHtml, args);
        string plain = TemplateRenderer.Substitute(email.PlainText ?? template.InApp.Body, args);

        return new RenderedMessage(subject, fullHtml, plain);
    }

    private static string RenderInnerHtml(EmailTemplate email, TemplateArgs args)
    {
        string htmlTemplate = EmailResourceLoader.Load(email.HtmlBodyResource);
        // HTML-escape только значений args; шаблон — наш контролируемый HTML.
        return TemplateRenderer.Substitute(htmlTemplate, args, WebUtility.HtmlEncode);
    }

    private static string WrapInLayout(string innerHtml, TemplateArgs args)
    {
        string layout = EmailResourceLoader.Load(LAYOUT_RESOURCE);

        // Сначала меняем слот на sentinel, чтобы TemplateRenderer не съел его при подстановке
        // (даже если кто-то передаст arg с ключом "body"). Layout-args без HTML-escape —
        // это конфиг (frontendUrl), HtmlEncode сломает URL'ы с query-string (& → &amp;).
        string layoutWithSentinel = layout.Replace(
            LAYOUT_BODY_PLACEHOLDER, BODY_SENTINEL, StringComparison.Ordinal);
        string layoutWithArgs = TemplateRenderer.Substitute(layoutWithSentinel, args);

        return layoutWithArgs.Replace(BODY_SENTINEL, innerHtml, StringComparison.Ordinal);
    }
}
