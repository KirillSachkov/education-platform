namespace Shared.Navigation;

/// <summary>
/// Stable short-values типов уведомлений, синхронизированные с
/// <c>NotificationService.Domain.Notifications.NotificationType</c>. Держим константы здесь,
/// чтобы <see cref="PlatformLinkBuilder"/> не тянул Domain-зависимость.
///
/// ⚠️ При добавлении нового типа в Domain enum — обновлять также тут (по тому же числу).
/// Номера stable — не менять, они идут в <c>notifications.type</c> колонку БД и в events.
/// </summary>
public static class NotificationTypes
{
    public const short WELCOME = 1;
    public const short COURSE_ENROLLED = 2;
    public const short MATERIAL_PUBLISHED = 3;
    public const short ISSUE_CREATED = 4;
    public const short ISSUE_SUBMISSION_APPROVED = 5;
    public const short ISSUE_SUBMISSION_CHANGES_REQUESTED = 6;
    public const short AUTHOR_ANNOUNCEMENT = 7;
    public const short TELEGRAM_LINKED = 8;
    public const short ISSUE_SUBMISSION_AWAITING_REVIEW = 9;
    public const short COMMENT_REPLIED = 10;
    public const short COMMENT_ON_OWN_CONTENT = 11;
    public const short ISSUE_PUBLISHED = 12;
    public const short PLAN_GRANT_RECEIVED = 13;
    public const short AUTHOR_HELP_REQUESTED = 14;
    public const short PLAN_GRANT_AUTHOR_SALE = 15;
    public const short WEEKLY_DIGEST = 16;
    public const short AI_REVIEW_OVERSIZED_SKIPPED = 17;
    public const short USER_LEVELED_UP = 18;
    public const short LEVEL_TEST_INVITE = 19;
    public const short TRIAL_EXPIRY_APPROACHING = 20;
    public const short TG_JOIN_REMINDER = 21;
    public const short VIDEO_AUTO_PROCESSING_FAILED = 22;
    public const short ACCESS_EXPIRED = 23;
    public const short ISSUE_AUTHOR_QUESTION = 24;
    public const short EMAIL_LOGIN_NOTICE = 25;
    public const short LINK_ACCOUNTS_NUDGE = 26;
    public const short STUDENT_PR_QUESTION_ASKED = 27;
    public const short SUBSCRIPTION_RENEWED = 28;
    public const short SUBSCRIPTION_RENEWAL_PROBLEM = 29;
    public const short SUBSCRIPTION_RENEWAL_CANCELLED = 30;
}
