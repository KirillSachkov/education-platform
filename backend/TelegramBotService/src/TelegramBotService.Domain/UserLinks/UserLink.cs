using CSharpFunctionalExtensions;
using SharedKernel;

namespace TelegramBotService.Domain.UserLinks;

/// <summary>
///     Связь Telegram-аккаунта с аккаунтом платформы / Link between Telegram account and platform account.
/// </summary>
public sealed class UserLink
{
    /// <summary>
    ///     Максимальная длина Telegram username / Maximum Telegram username length.
    /// </summary>
    public const int TELEGRAM_USERNAME_MAX_LENGTH = 64;

    /// <summary>
    /// Максимальная длина <see cref="BlockedReason"/> (стабильный код причины блокировки).
    /// </summary>
    public const int BLOCKED_REASON_MAX_LENGTH = 64;

    private UserLink(
        long telegramUserId,
        Guid platformUserId,
        string? telegramUsername,
        DateTime linkedAt)
    {
        TelegramUserId = telegramUserId;
        PlatformUserId = platformUserId;
        TelegramUsername = telegramUsername;
        LinkedAt = linkedAt;
        BlockedAt = null;
        BlockedReason = null;
    }

    // EF Core
    private UserLink()
    {
    }

    /// <summary>
    ///     Telegram numeric user id (PK, = chat id в личке) / Telegram numeric user id (PK).
    /// </summary>
    public long TelegramUserId { get; private set; }

    /// <summary>
    ///     Идентификатор пользователя платформы / Platform user identifier.
    /// </summary>
    public Guid PlatformUserId { get; private set; }

    /// <summary>
    ///     Telegram <c>@username</c> — опциональный, только для display / Telegram username (display only).
    /// </summary>
    public string? TelegramUsername { get; private set; }

    /// <summary>
    ///     Дата и время установки связи (UTC) / Link creation date and time (UTC).
    /// </summary>
    public DateTime LinkedAt { get; private set; }

    /// <summary>
    /// Soft-block: link заблокирован Telegram'ом (юзер заблокировал бота, удалил
    /// аккаунт). NULL — link активен. Когда задан — handler пропускает send и
    /// публикует <c>skipped</c> delivery с error_code из <see cref="BlockedReason"/>.
    /// Раньше при <c>bot_blocked</c>/<c>chat_not_found</c> link удалялся целиком —
    /// если юзер потом разблокирует бота, новый link придётся создавать заново
    /// через <c>/start</c> с link-токеном (UX боль). Теперь блокировка обратима:
    /// при следующем <c>/start</c> от того же telegram_user_id link автоматически
    /// «восстанавливается» через <see cref="Unblock"/>.
    /// </summary>
    public DateTime? BlockedAt { get; private set; }

    /// <summary>
    /// Стабильный код причины блокировки (например <c>bot_blocked</c>,
    /// <c>chat_not_found</c>) — попадает в delivery event'ы и метрики.
    /// </summary>
    public string? BlockedReason { get; private set; }

    /// <summary>Активный (не soft-blocked) link.</summary>
    public bool IsActive => BlockedAt is null;

    /// <summary>
    ///     Меняет <see cref="TelegramUsername"/>. Используется при refresh'е на /start
    ///     если юзер сменил Telegram-username. Чисто данные display-уровня — каскадных
    ///     эффектов нет.
    /// </summary>
    public void UpdateTelegramUsername(string? telegramUsername)
    {
        if (!string.IsNullOrWhiteSpace(telegramUsername) &&
            telegramUsername.Length > TELEGRAM_USERNAME_MAX_LENGTH)
        {
            telegramUsername = telegramUsername[..TELEGRAM_USERNAME_MAX_LENGTH];
        }

        TelegramUsername = telegramUsername;
    }

    /// <summary>
    /// Помечает link как заблокированный с указанной причиной. Идемпотентно —
    /// повторный вызов с той же причиной no-op'ится.
    /// </summary>
    public void Block(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            reason = "unknown";

        if (reason.Length > BLOCKED_REASON_MAX_LENGTH)
            reason = reason[..BLOCKED_REASON_MAX_LENGTH];

        BlockedAt = DateTime.UtcNow;
        BlockedReason = reason;
    }

    /// <summary>
    /// Снимает soft-block. Вызывается из <c>LinkAccountHandler</c> при <c>/start</c>
    /// от того же telegram_user_id — юзер вернулся, восстанавливаем link.
    /// </summary>
    public void Unblock()
    {
        BlockedAt = null;
        BlockedReason = null;
    }

    public static Result<UserLink, Error> Create(
        long telegramUserId,
        Guid platformUserId,
        string? telegramUsername)
    {
        if (telegramUserId <= 0)
            return GeneralErrors.ValueIsInvalid("telegram.user.id");

        if (platformUserId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("platform.user.id");

        string? normalizedUsername = telegramUsername;
        if (!string.IsNullOrWhiteSpace(normalizedUsername) &&
            normalizedUsername.Length > TELEGRAM_USERNAME_MAX_LENGTH)
        {
            normalizedUsername = normalizedUsername[..TELEGRAM_USERNAME_MAX_LENGTH];
        }

        return new UserLink(
            telegramUserId,
            platformUserId,
            normalizedUsername,
            DateTime.UtcNow);
    }
}
