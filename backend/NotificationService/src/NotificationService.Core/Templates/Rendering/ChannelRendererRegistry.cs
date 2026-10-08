using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Templates.Rendering;

/// <summary>
/// Маппинг <see cref="NotificationChannel"/> → <see cref="IChannelRenderer"/>.
/// Собирается из всех зарегистрированных в DI renderer'ов.
/// </summary>
public sealed class ChannelRendererRegistry
{
    private readonly Dictionary<NotificationChannel, IChannelRenderer> _renderers;

    public ChannelRendererRegistry(IEnumerable<IChannelRenderer> renderers)
    {
        _renderers = [];
        foreach (IChannelRenderer renderer in renderers)
        {
            if (_renderers.ContainsKey(renderer.Channel))
                throw new InvalidOperationException($"Renderer для канала {renderer.Channel} зарегистрирован несколько раз.");
            _renderers[renderer.Channel] = renderer;
        }
    }

    public RenderedMessage Render(NotificationChannel channel, NotificationTemplate template, TemplateArgs args)
    {
        if (!_renderers.TryGetValue(channel, out IChannelRenderer? renderer))
            throw new InvalidOperationException($"Не зарегистрирован renderer для канала {channel}.");

        return renderer.Render(template, args);
    }
}
