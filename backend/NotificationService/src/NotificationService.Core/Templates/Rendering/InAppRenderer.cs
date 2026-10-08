using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Templates.Rendering;

/// <summary>
/// Рендер InApp-уведомления / Renders the in-app notification.
/// Плоский текст, без markdown/html. Минимализм — колокольчик и drawer.
/// </summary>
public sealed class InAppRenderer : IChannelRenderer
{
    public NotificationChannel Channel => NotificationChannel.InApp;

    public RenderedMessage Render(NotificationTemplate template, TemplateArgs args)
    {
        string title = TemplateRenderer.Substitute(template.InApp.Title, args);
        string body = TemplateRenderer.Substitute(template.InApp.Body, args);
        return new RenderedMessage(title, body);
    }
}
