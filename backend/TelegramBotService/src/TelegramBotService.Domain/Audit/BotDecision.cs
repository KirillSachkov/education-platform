namespace TelegramBotService.Domain.Audit;

/// <summary>
///     Аудит-запись о решении, которое принял бот: approve/decline join request,
///     auto-kick на enrollment-revoke, grant-enrollment по claim из чата.
///     Нужно для разбора инцидентов («почему юзер не попал в чат / почему его выкинуло»).
///     Retention — 90 дней через периодический cleanup.
/// </summary>
public sealed class BotDecision
{
    public const int REASON_MAX_LENGTH = 256;
    public const int DECISION_MAX_LENGTH = 64;

    private BotDecision()
    {
        Decision = null!;
    }

    public BotDecision(
        Guid id,
        long telegramChatId,
        long telegramUserId,
        string decision,
        string? reason,
        Guid? planId,
        DateTime createdAt)
    {
        Id = id;
        TelegramChatId = telegramChatId;
        TelegramUserId = telegramUserId;
        Decision = decision;
        Reason = reason;
        PlanId = planId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public long TelegramChatId { get; private set; }
    public long TelegramUserId { get; private set; }
    public string Decision { get; private set; }
    public string? Reason { get; private set; }
    public Guid? PlanId { get; private set; }
    public DateTime CreatedAt { get; private set; }
}

/// <summary>
///     Известные значения <see cref="BotDecision.Decision"/>. Stable contract — не менять,
///     только добавлять.
/// </summary>
public static class BotDecisions
{
    public const string JOIN_REQUEST_APPROVED = "join_request.approved";
    public const string JOIN_REQUEST_DECLINED_NO_BINDING = "join_request.declined.no_binding";
    public const string JOIN_REQUEST_DECLINED_NO_LINK = "join_request.declined.no_link";
    public const string JOIN_REQUEST_DECLINED_NO_ENROLLMENT = "join_request.declined.no_enrollment";
    public const string JOIN_REQUEST_DECLINED_PROGRESS_ERROR = "join_request.declined.progress_error";
    public const string AUTO_KICK_ENROLLMENT_REVOKED = "auto_kick.enrollment_revoked";
    public const string CLAIM_GRANTED = "claim.granted";
    public const string CLAIM_ALREADY_ENROLLED = "claim.already_enrolled";
    public const string CLAIM_DECLINED_NOT_MEMBER = "claim.declined.not_member";
    public const string CLAIM_DECLINED_NO_BINDING = "claim.declined.no_binding";
}
