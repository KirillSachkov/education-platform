using NotificationService.Core.Templates.Parts;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Templates;

/// <summary>
/// Иммутабельный шаблон уведомления / Immutable notification template.
///
/// Композиция per-channel parts: InApp обязателен, Telegram и Email — опциональны.
/// Если канал-часть равна <c>null</c>, dispatcher не предложит этот канал даже если он
/// включён у пользователя и упомянут в <see cref="DefaultChannels"/>.
///
/// Метаданные (<see cref="Type"/>, <see cref="DefaultChannels"/>) лежат в самом шаблоне —
/// handler через <see cref="Notifications.NotificationRequest.From"/> их подхватывает,
/// не дублируя в каждом вызове.
/// </summary>
public sealed record NotificationTemplate
{
    public NotificationTemplate(
        string id,
        NotificationType type,
        NotificationChannel defaultChannels,
        InAppTemplate inApp,
        TelegramTemplate? telegram = null,
        EmailTemplate? email = null,
        NotificationChannel forcedChannels = NotificationChannel.None)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(inApp);

        // Sanity: DefaultChannels не должен предлагать канал, для которого нет part'а.
        // InApp всегда есть; Telegram/Email — только если part задан.
        if ((defaultChannels & NotificationChannel.Telegram) != NotificationChannel.None && telegram is null)
            throw new ArgumentException(
                $"Template '{id}' has Telegram in DefaultChannels but no Telegram part.",
                nameof(defaultChannels));
        if ((defaultChannels & NotificationChannel.Email) != NotificationChannel.None && email is null)
            throw new ArgumentException(
                $"Template '{id}' has Email in DefaultChannels but no Email part.",
                nameof(defaultChannels));

        // Sanity: форсить можно только каналы, заявленные в DefaultChannels (что гарантирует
        // наличие part'а). Форс-канал вне запрошенного набора — ошибка конфигурации шаблона.
        if ((forcedChannels & defaultChannels) != forcedChannels)
            throw new ArgumentException(
                $"Template '{id}' has ForcedChannels outside of DefaultChannels.",
                nameof(forcedChannels));

        Id = id;
        Type = type;
        DefaultChannels = defaultChannels;
        InApp = inApp;
        Telegram = telegram;
        Email = email;
        ForcedChannels = forcedChannels;
    }

    /// <summary>Стабильный идентификатор шаблона (хранится в БД).</summary>
    public string Id { get; }

    /// <summary>Тип уведомления.</summary>
    public NotificationType Type { get; }

    /// <summary>Каналы, включённые «из коробки». Пересекается с UserNotificationChannels на dispatch'е.</summary>
    public NotificationChannel DefaultChannels { get; }

    /// <summary>Часть для сайта — обязательна.</summary>
    public InAppTemplate InApp { get; }

    /// <summary>Часть для Telegram (опц.). <c>null</c> = не доставлять в Telegram.</summary>
    public TelegramTemplate? Telegram { get; }

    /// <summary>Часть для email (опц.). <c>null</c> = не доставлять по email.</summary>
    public EmailTemplate? Email { get; }

    /// <summary>
    /// Каналы, доставляемые ПОВЕРХ пользовательских настроек (критичные уведомления об
    /// аккаунте, #704): dispatcher не режет их канальным mask'ом
    /// (<c>UserNotificationChannels</c>) и не применяет к такому шаблону per-type opt-out.
    /// Узкий механизм — включается только явным объявлением на конкретном шаблоне
    /// (сейчас единственный: <c>EmailLoginNotice</c> — «вход теперь по почте», юзер обязан
    /// узнать как войти после отключения GitHub-входа). Обычные шаблоны оставляют
    /// <see cref="NotificationChannel.None"/> — все opt-out'ы работают штатно.
    /// </summary>
    public NotificationChannel ForcedChannels { get; }

    /// <summary>
    /// Поддерживает ли шаблон доставку в указанный канал (есть соответствующая part).
    /// </summary>
    public bool Supports(NotificationChannel channel) => channel switch
    {
        NotificationChannel.InApp => true,
        NotificationChannel.Telegram => Telegram is not null,
        NotificationChannel.Email => Email is not null,
        _ => false,
    };
}
