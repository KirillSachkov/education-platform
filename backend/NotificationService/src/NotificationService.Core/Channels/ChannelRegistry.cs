using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Channels;

/// <summary>
/// Маппинг <see cref="NotificationChannel"/> → реализация <see cref="INotificationChannel"/>.
/// Построен из всех <see cref="INotificationChannel"/>, зарегистрированных в DI.
/// </summary>
public sealed class ChannelRegistry
{
    private readonly Dictionary<NotificationChannel, INotificationChannel> _channels;

    public ChannelRegistry(IEnumerable<INotificationChannel> channels)
    {
        _channels = [];
        foreach (INotificationChannel channel in channels)
        {
            if (_channels.ContainsKey(channel.Type))
                throw new InvalidOperationException($"Канал {channel.Type} зарегистрирован несколько раз.");
            _channels[channel.Type] = channel;
        }
    }

    public bool TryGet(NotificationChannel type, out INotificationChannel? channel)
    {
        bool found = _channels.TryGetValue(type, out INotificationChannel? result);
        channel = result;
        return found;
    }

    public IReadOnlyCollection<NotificationChannel> RegisteredChannels => _channels.Keys;
}
