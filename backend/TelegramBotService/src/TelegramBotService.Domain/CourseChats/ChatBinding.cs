using CSharpFunctionalExtensions;
using SharedKernel;

namespace TelegramBotService.Domain.CourseChats;

/// <summary>
///     Привязка плана доступа к Telegram-чату (группе или каналу).
///     Один план может иметь несколько чатов; один чат может быть привязан к нескольким планам
///     (даже разных авторов). Бот должен быть админом в чате с правами
///     <c>can_invite_users</c> и <c>can_restrict_members</c>.
/// </summary>
public sealed class ChatBinding
{
    public const int CHAT_TITLE_MAX_LENGTH = 256;
    public const int INVITE_LINK_MAX_LENGTH = 256;
    public const int VALIDATION_ERROR_MAX_LENGTH = 500;

    private ChatBinding(
        Guid id,
        Guid planId,
        long telegramChatId,
        ChatType chatType,
        string? chatTitle,
        string inviteLink,
        bool enrollmentGrantsMembership,
        bool membershipGrantsEnrollment,
        bool autoKickOnRevoke,
        bool enforceMembership,
        Guid createdBy,
        DateTime createdAt)
    {
        Id = id;
        PlanId = planId;
        TelegramChatId = telegramChatId;
        ChatType = chatType;
        ChatTitle = chatTitle;
        InviteLink = inviteLink;
        EnrollmentGrantsMembership = enrollmentGrantsMembership;
        MembershipGrantsEnrollment = membershipGrantsEnrollment;
        AutoKickOnRevoke = autoKickOnRevoke;
        EnforceMembership = enforceMembership;
        CreatedBy = createdBy;
        CreatedAt = createdAt;

        // Новые binding'и считаем здоровыми — права уже проверены в BindChat use-case'е.
        IsHealthy = true;
        LastValidatedAt = createdAt;
    }

    // EF Core
    private ChatBinding()
    {
        InviteLink = null!;
    }

    public Guid Id { get; private set; }
    public Guid PlanId { get; private set; }
    public long TelegramChatId { get; private set; }
    public ChatType ChatType { get; private set; }
    public string? ChatTitle { get; private set; }
    public string InviteLink { get; private set; }
    public bool EnrollmentGrantsMembership { get; private set; }
    public bool MembershipGrantsEnrollment { get; private set; }
    public bool AutoKickOnRevoke { get; private set; }

    /// <summary>
    ///     Если true, при ручном добавлении юзера в чат (admin'ом или через старый invite link)
    ///     бот проверяет наличие STANDARD-зачисления и кикает не-членов. По умолчанию false —
    ///     opt-in, потому что социально болезненно (выгоняем людей которых руками привёл админ).
    /// </summary>
    public bool EnforceMembership { get; private set; }

    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    ///     Здоров ли binding: бот в чате как админ с нужными правами. Выставляется
    ///     periodic'ом <see cref="ChatBindingHealthCheckService"/> и
    ///     <see cref="ChatBindingHealthService"/> при кике / снятии прав. False означает
    ///     «F1/F2/F6 не работают» — admin'у нужно либо вернуть права, либо unbind.
    /// </summary>
    public bool IsHealthy { get; private set; }

    /// <summary>
    ///     UTC момент последней проверки прав бота в чате. NULL только для legacy-row'ов
    ///     до миграции AddChatBindingHealth.
    /// </summary>
    public DateTime? LastValidatedAt { get; private set; }

    /// <summary>
    ///     Описание последней ошибки валидации (например «bot is not admin» / «missing
    ///     can_invite_users»). NULL когда binding здоров.
    /// </summary>
    public string? LastValidationError { get; private set; }

    /// <summary>
    ///     Telegram message_id закреплённого claim-объявления, которое бот запостил в группу
    ///     (фича A, #410). NULL = ещё не постили. Идемпотентность: повторные триггеры
    ///     (bind / добавление бота) не постят второе объявление.
    /// </summary>
    public int? AnnouncementMessageId { get; private set; }

    /// <summary>
    ///     Bind-time дефолт для <see cref="AutoKickOnRevoke"/> по типу чата (#687):
    ///     курсовые ГРУППЫ (<see cref="ChatType.SUPERGROUP"/>) авто-кикают участника при
    ///     revoke/expire доступа по умолчанию — надёжное удаление истёкших пользователей.
    ///     КАНАЛЫ (<see cref="ChatType.CHANNEL"/>) сохраняют opt-in (kick на канале не
    ///     поддерживается / best-effort). Применяется только на bind'е (см. <c>BindChat</c>);
    ///     админ может переопределить per-binding через PATCH флагов (<see cref="UpdateFlags"/>).
    /// </summary>
    public static bool DefaultAutoKickOnRevoke(ChatType chatType) =>
        chatType == ChatType.SUPERGROUP;

    public static Result<ChatBinding, Error> Create(
        Guid id,
        Guid planId,
        long telegramChatId,
        ChatType chatType,
        string? chatTitle,
        string inviteLink,
        bool enrollmentGrantsMembership,
        bool membershipGrantsEnrollment,
        bool autoKickOnRevoke,
        bool enforceMembership,
        Guid createdBy)
    {
        if (id == Guid.Empty)
            return GeneralErrors.ValueIsRequired("chat.binding.id");

        if (planId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("plan.id");

        if (telegramChatId == 0)
            return GeneralErrors.ValueIsInvalid("telegram.chat.id");

        if (string.IsNullOrWhiteSpace(inviteLink))
            return GeneralErrors.ValueIsRequired("invite.link");

        if (createdBy == Guid.Empty)
            return GeneralErrors.ValueIsRequired("created.by");

        string? normalizedTitle = chatTitle;
        if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
            normalizedTitle.Length > CHAT_TITLE_MAX_LENGTH)
        {
            normalizedTitle = normalizedTitle[..CHAT_TITLE_MAX_LENGTH];
        }

        if (inviteLink.Length > INVITE_LINK_MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid("invite.link");

        return new ChatBinding(
            id,
            planId,
            telegramChatId,
            chatType,
            normalizedTitle,
            inviteLink,
            enrollmentGrantsMembership,
            membershipGrantsEnrollment,
            autoKickOnRevoke,
            enforceMembership,
            createdBy,
            DateTime.UtcNow);
    }

    public void UpdateFlags(
        bool enrollmentGrantsMembership,
        bool membershipGrantsEnrollment,
        bool autoKickOnRevoke,
        bool enforceMembership)
    {
        EnrollmentGrantsMembership = enrollmentGrantsMembership;
        MembershipGrantsEnrollment = membershipGrantsEnrollment;
        AutoKickOnRevoke = autoKickOnRevoke;
        EnforceMembership = enforceMembership;
    }

    /// <summary>
    ///     Помечает binding здоровым (бот всё ещё админ с нужными правами).
    ///     Сбрасывает <see cref="LastValidationError"/>.
    /// </summary>
    public void MarkHealthy()
    {
        IsHealthy = true;
        LastValidationError = null;
        LastValidatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Помечает binding нездоровым с описанием причины (бот не админ / снят /
    ///     выкинут / нет прав). F1/F2/F6 действия для этого binding'а админ должен
    ///     либо починить, либо unbind.
    /// </summary>
    public void MarkUnhealthy(string reason)
    {
        IsHealthy = false;
        LastValidationError = string.IsNullOrWhiteSpace(reason)
            ? "unknown"
            : reason.Length > VALIDATION_ERROR_MAX_LENGTH
                ? reason[..VALIDATION_ERROR_MAX_LENGTH]
                : reason;
        LastValidatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Фиксирует, что claim-объявление запощено в группу (с его Telegram message_id).
    ///     Делает дальнейшие попытки поста идемпотентными (фича A, #410).
    /// </summary>
    public void MarkAnnouncementPosted(int messageId) => AnnouncementMessageId = messageId;

    public void UpdateChatMetadata(string? chatTitle, string inviteLink)
    {
        if (string.IsNullOrWhiteSpace(inviteLink))
            return;

        string? normalizedTitle = chatTitle;
        if (!string.IsNullOrWhiteSpace(normalizedTitle) &&
            normalizedTitle.Length > CHAT_TITLE_MAX_LENGTH)
        {
            normalizedTitle = normalizedTitle[..CHAT_TITLE_MAX_LENGTH];
        }

        ChatTitle = normalizedTitle;
        InviteLink = inviteLink;
    }
}
