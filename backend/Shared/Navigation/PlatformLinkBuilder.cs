using System.Text.Json;

namespace Shared.Navigation;

/// <summary>
/// Единая точка правды для URL'ов в уведомлениях и во всех исходящих ссылках платформы.
/// Pure static helpers — без DI, без Domain-зависимостей, без конфигурации
/// (<c>frontendBaseUrl</c> передаётся параметром).
///
/// Два типа ссылок:
/// <list type="bullet">
/// <item><b>OpenUrl</b> — короткая клик-ссылка <c>{base}/n/{notificationId}</c> для вставки
/// в email / telegram / push. Клик ведёт на proxy <c>GET /notifications/{id}/open</c>, который
/// mark-as-read и 302-редиректит на target URL.</item>
/// <item><b>TargetUrl</b> — конкретный URL по <c>(type, payload)</c>: страница курса/материала/
/// задачи/ревью/комментария. Использует proxy-endpoint внутри сервиса.</item>
/// </list>
///
/// Зачем в Shared: нужен и NotificationService'у (dispatcher + proxy endpoint),
/// и TelegramBotService'у (если потребуется строить ссылки из бота), и frontend'у —
/// контракт один, легко держать в синхронности через <see cref="NotificationTypes"/> константы.
/// </summary>
public static class PlatformLinkBuilder
{
    /// <summary>
    /// Короткая клик-ссылка для email/telegram — идёт через proxy-endpoint, который
    /// mark-as-read и 302 на target. Возвращает string (не Uri), т.к. используется напрямую
    /// в template substitution.
    /// </summary>
#pragma warning disable CA1055  // аргумент template substitution — string, не Uri
    public static string BuildOpenUrl(string frontendBaseUrl, Guid notificationId) =>
#pragma warning restore CA1055
        $"{NormalizeBase(frontendBaseUrl)}/n/{notificationId}";

    /// <summary>
    /// Полный target URL по типу уведомления + payload. Phase 5 (#308) — flat URLs.
    /// Payload должен содержать route-контекст: <c>courseSlug</c> для course-routes;
    /// <c>authorSlug</c> (если присутствует в legacy payload) игнорируется — frontend
    /// middleware 308-редиректит уже-отправленные /@slug/... URL'ы на flat.
    /// Fallback на корень платформы если payload не содержит нужных ключей.
    /// </summary>
#pragma warning disable CA1055  // возвращает string для прямого использования в Results.Redirect / template substitution
    public static string BuildTargetUrl(string frontendBaseUrl, short notificationType, string? payloadJson)
#pragma warning restore CA1055
    {
        string baseUrl = NormalizeBase(frontendBaseUrl);
        PayloadDict payload = ParsePayload(payloadJson);

        return notificationType switch
        {
            NotificationTypes.WELCOME =>
                $"{baseUrl}/",
            NotificationTypes.COURSE_ENROLLED when payload.CourseSlug is not null =>
                $"{baseUrl}/courses/{payload.CourseSlug}",
            NotificationTypes.MATERIAL_PUBLISHED when payload is { CourseSlug: not null, MaterialId: Guid mid } =>
                $"{baseUrl}/courses/{payload.CourseSlug}/learn/{mid}",
            NotificationTypes.ISSUE_CREATED when payload is { CourseSlug: not null, IssueId: Guid iid } =>
                $"{baseUrl}/courses/{payload.CourseSlug}/issues/{iid}",
            NotificationTypes.ISSUE_PUBLISHED when payload is { CourseSlug: not null, IssueId: Guid pubIid } =>
                $"{baseUrl}/courses/{payload.CourseSlug}/issues/{pubIid}",
            NotificationTypes.ISSUE_SUBMISSION_APPROVED when payload is { CourseSlug: not null, IssueId: Guid aiid } =>
                $"{baseUrl}/courses/{payload.CourseSlug}/issues/{aiid}",
            NotificationTypes.ISSUE_SUBMISSION_CHANGES_REQUESTED when payload is { CourseSlug: not null, IssueId: Guid ciid } =>
                $"{baseUrl}/courses/{payload.CourseSlug}/issues/{ciid}",
            _ when IsReviewNotification(notificationType)
                && payload.SubmissionId is Guid reviewSubmissionId =>
                BuildReviewQueueUrl(baseUrl, reviewSubmissionId),
            _ when IsReviewNotification(notificationType) =>
                $"{baseUrl}/author/review",
            // #693 — вопрос автору по заданию ДО сабмишена: клик ведёт на саму страницу задания
            // (сабмишена ещё нет, author-review inbox пуст). Нет курса/слага → fallback /home.
            NotificationTypes.ISSUE_AUTHOR_QUESTION when payload is { CourseSlug: not null, IssueId: Guid aqIid } =>
                $"{baseUrl}/courses/{payload.CourseSlug}/issues/{aqIid}",
            NotificationTypes.ISSUE_AUTHOR_QUESTION =>
                $"{baseUrl}/home",
            // #648 — авто-обработка видео упала: клик ведёт в редактор материала,
            // где автор видит панель «AI-обработка видео» и может перезапустить вручную.
            NotificationTypes.VIDEO_AUTO_PROCESSING_FAILED when payload.MaterialId is Guid vMid =>
                $"{baseUrl}/author/knowledge-base/edit/{vMid}",
            NotificationTypes.VIDEO_AUTO_PROCESSING_FAILED =>
                $"{baseUrl}/author/knowledge-base",
            NotificationTypes.AUTHOR_ANNOUNCEMENT when payload is { TargetType: "course", CourseSlug: not null } =>
                $"{baseUrl}/courses/{payload.CourseSlug}",
            NotificationTypes.AUTHOR_ANNOUNCEMENT when string.Equals(payload.TargetType, "author", StringComparison.Ordinal) =>
                $"{baseUrl}/home",
            NotificationTypes.TELEGRAM_LINKED =>
                $"{baseUrl}/settings",
            NotificationTypes.PLAN_GRANT_RECEIVED when payload.CourseSlug is not null =>
                $"{baseUrl}/courses/{payload.CourseSlug}",
            NotificationTypes.PLAN_GRANT_RECEIVED =>
                $"{baseUrl}/home",
            NotificationTypes.PLAN_GRANT_AUTHOR_SALE =>
                $"{baseUrl}/admin/payments",
            // #580 — пробный доступ истекает: клик ведёт в каталог планов, где оформляется
            // апгрейд до полного доступа (upgrade-quote показывает цену с зачётом пробного).
            NotificationTypes.TRIAL_EXPIRY_APPROACHING =>
                $"{baseUrl}/pricing",
            // #687 — доступ истёк: клик ведёт в каталог планов, где оформляется продление
            // (при доплате зачтётся уже оплаченное).
            NotificationTypes.ACCESS_EXPIRED =>
                $"{baseUrl}/pricing",
            // #616 — нудж на вступление в Telegram-группу плана: клик ведёт на флоу
            // «привязка Telegram → вступление в чат» (страница ST-4 читает ?plan=).
            NotificationTypes.TG_JOIN_REMINDER when payload.PlanId is Guid joinPlanId =>
                $"{baseUrl}/telegram/join?plan={joinPlanId}",
            NotificationTypes.TG_JOIN_REMINDER =>
                $"{baseUrl}/telegram/join",
            NotificationTypes.WEEKLY_DIGEST =>
                $"{baseUrl}/home",
            NotificationTypes.USER_LEVELED_UP =>
                $"{baseUrl}/home",
            NotificationTypes.LEVEL_TEST_INVITE =>
                $"{baseUrl}/level-test",
            // #704 — «вход теперь по почте»: клик ведёт на страницу входа (копирайт §2).
            NotificationTypes.EMAIL_LOGIN_NOTICE =>
                $"{baseUrl}/login",
            // #704 — «привяжите аккаунты»: клик ведёт в настройки интеграций.
            NotificationTypes.LINK_ACCOUNTS_NUDGE =>
                $"{baseUrl}/settings/integrations",
            NotificationTypes.SUBSCRIPTION_RENEWED
                or NotificationTypes.SUBSCRIPTION_RENEWAL_PROBLEM
                or NotificationTypes.SUBSCRIPTION_RENEWAL_CANCELLED =>
                $"{baseUrl}/my-plans",
            NotificationTypes.COMMENT_REPLIED when payload is { CommentId: Guid repCid, EntityType: string repEt, EntityId: Guid repEid } =>
                BuildCommentThreadUrl(baseUrl, repEt, repEid, repCid, payload.CourseSlug),
            NotificationTypes.COMMENT_ON_OWN_CONTENT when payload is { CommentId: Guid ownCid, EntityType: string ownEt, EntityId: Guid ownEid } =>
                BuildCommentThreadUrl(baseUrl, ownEt, ownEid, ownCid, payload.CourseSlug),
            _ => $"{baseUrl}/",
        };
    }

#pragma warning disable CA1055
    public static string ResolveTargetUrl(string frontendBaseUrl, short notificationType, string? payloadJson)
#pragma warning restore CA1055
    {
        PayloadDict payload = ParsePayload(payloadJson);
        if (IsReviewNotification(notificationType) && payload.SubmissionId is Guid submissionId)
        {
            return BuildReviewQueueUrl(NormalizeBase(frontendBaseUrl), submissionId);
        }

        string? baked = TryReadTargetUrl(payloadJson);
        return string.IsNullOrWhiteSpace(baked) ? $"{NormalizeBase(frontendBaseUrl)}/" : baked;
    }

    private static bool IsReviewNotification(short notificationType) =>
        notificationType is NotificationTypes.ISSUE_SUBMISSION_AWAITING_REVIEW
            or NotificationTypes.AUTHOR_HELP_REQUESTED
            or NotificationTypes.AI_REVIEW_OVERSIZED_SKIPPED
            or NotificationTypes.STUDENT_PR_QUESTION_ASKED;

    private static string BuildCommentThreadUrl(
        string baseUrl, string entityType, Guid entityId, Guid commentId, string? courseSlug)
    {
        // Defense-in-depth: контракт CommentCreated.EntityType документирован как lowercase
        // ("material" / "issue" / "course"), но исторически некоторые publisher'ы шлют
        // PascalCase из enum.ToString(). Нормализуем на чтение, чтобы старые payload'ы
        // и любой будущий consumer не зависели от casing'а на той стороне.
        string normalized = entityType.ToLowerInvariant();
        return normalized switch
        {
            "material" when courseSlug is not null =>
                $"{baseUrl}/courses/{courseSlug}/learn/{entityId}?comment={commentId}",
            "issue" when courseSlug is not null =>
                $"{baseUrl}/courses/{courseSlug}/issues/{entityId}?comment={commentId}",
            "course" when courseSlug is not null =>
                $"{baseUrl}/courses/{courseSlug}?comment={commentId}",
            _ =>
                $"{baseUrl}/",
        };
    }

    private static string BuildReviewQueueUrl(string baseUrl, Guid submissionId) =>
        $"{baseUrl}/author/review?submissionId={submissionId}";

    private static string NormalizeBase(string frontendBaseUrl) =>
        string.IsNullOrWhiteSpace(frontendBaseUrl) ? "" : frontendBaseUrl.TrimEnd('/');

    /// <summary>
    /// Безопасный парс jsonb-payload'а. Отсутствующие ключи возвращают <c>null</c>, не кидают.
    /// </summary>
    private static PayloadDict ParsePayload(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || string.Equals(payloadJson, "{}", StringComparison.Ordinal))
            return default;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            JsonElement root = doc.RootElement;
            return new PayloadDict(
                CourseId: TryGetGuid(root, "courseId"),
                PlanId: TryGetGuid(root, "planId"),
                MaterialId: TryGetGuid(root, "materialId"),
                IssueId: TryGetGuid(root, "issueId"),
                SubmissionId: TryGetGuid(root, "submissionId"),
                CommentId: TryGetGuid(root, "commentId"),
                TargetId: TryGetGuid(root, "targetId"),
                EntityId: TryGetGuid(root, "entityId"),
                CourseSlug: TryGetString(root, "courseSlug"),
                TargetType: TryGetString(root, "targetType"),
                EntityType: TryGetString(root, "entityType"));
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static Guid? TryGetGuid(JsonElement root, string propName) =>
        root.TryGetProperty(propName, out JsonElement el) &&
        el.ValueKind == JsonValueKind.String &&
        Guid.TryParse(el.GetString(), out Guid g)
            ? g
            : null;

    private static string? TryGetString(JsonElement root, string propName) =>
        root.TryGetProperty(propName, out JsonElement el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private static string? TryReadTargetUrl(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson) || string.Equals(payloadJson, "{}", StringComparison.Ordinal))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("targetUrl", out JsonElement el)
                && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private readonly record struct PayloadDict(
        Guid? CourseId = null,
        Guid? PlanId = null,
        Guid? MaterialId = null,
        Guid? IssueId = null,
        Guid? SubmissionId = null,
        Guid? CommentId = null,
        Guid? TargetId = null,
        Guid? EntityId = null,
        string? CourseSlug = null,
        string? TargetType = null,
        string? EntityType = null);
}
