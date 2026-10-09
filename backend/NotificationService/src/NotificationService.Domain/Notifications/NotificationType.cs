namespace NotificationService.Domain.Notifications;

/// <summary>
/// Тип уведомления / Notification type.
/// Stable enum values — используются для idempotency index и сериализации в events,
/// НЕ менять существующие номера.
/// </summary>
public enum NotificationType
{
    Welcome = 1,
    CourseEnrolled = 2,
    MaterialPublished = 3,
    IssueCreated = 4,
    IssueSubmissionApproved = 5,
    IssueSubmissionChangesRequested = 6,
    AuthorAnnouncement = 7,
    TelegramLinked = 8,
    IssueSubmissionAwaitingReview = 9,

    /// <summary>Ответ на твой коммент — recipient = автор parent-комментария.</summary>
    CommentReplied = 10,

    /// <summary>Новый коммент на твоей сущности (материал/задача/курс) — recipient = owner.</summary>
    CommentOnOwnContent = 11,

    /// <summary>Опубликовано новое задание в курсе — подписчикам курса.</summary>
    IssuePublished = 12,

    /// <summary>
    /// Пользователь получил доступ по плану (redeem invite, claim FREE, admin grant,
    /// future PURCHASE). Заменяет N per-course CourseEnrolled-уведомлений когда юзеру
    /// открывается несколько курсов одним plan-grant'ом.
    /// </summary>
    PlanGrantReceived = 13,

    /// <summary>
    /// Студент нажал «Позвать автора» по своему решению (#383) — recipient = автор курса.
    /// AI-ассистент не заменяет автора; студент явно подключает его, когда нужна живая помощь.
    /// </summary>
    AuthorHelpRequested = 14,

    /// <summary>
    /// Пользователь получил доступ к плану автора (purchase / invite / admin-grant / github-org /
    /// trial) — recipient = автор плана (#428). Несёт данные покупателя (имя, ник, email), чтобы
    /// автор видел, кто пришёл. Backfill-гранты (Source=MIGRATION) не уведомляют.
    /// </summary>
    PlanGrantAuthorSale = 15,

    /// <summary>
    /// Еженедельный дайджест «что нового за неделю» (#468) — агрегат собственных уведомлений
    /// получателя типов <see cref="MaterialPublished"/>/<see cref="IssuePublished"/> за последние
    /// 7 дней. Генерируется фоновым <c>WeeklyDigestService</c>, без новых RabbitMQ-событий.
    /// </summary>
    WeeklyDigest = 16,

    /// <summary>
    /// Большой PR (#546): авто-ран AI-проверки пропущен — reviewable diff выше hard-cap'ов
    /// AssignmentReviewService. Recipient = автор курса; CTA — запустить проверку вручную
    /// («Перепроверить» снимает cap и ревьюит PR целиком по частям).
    /// </summary>
    AiReviewOversizedSkipped = 17,

    /// <summary>Retired; value 18 remains reserved for persisted notifications.</summary>
#pragma warning disable S1133 // Persisted notification numbers are permanent compatibility slots.
    [Obsolete("Retired; reserved for persisted notification compatibility.")]
    UserLeveledUp = 18,

    /// <summary>Retired; value 19 remains reserved for persisted notifications.</summary>
    [Obsolete("Retired; reserved for persisted notification compatibility.")]
    LevelTestInvite = 19,
#pragma warning restore S1133

    /// <summary>
    /// Пробный (trial) доступ скоро истекает (#580) — recipient = сам пользователь.
    /// AccessService (фоновый <c>TrialExpiryReminderSweeper</c>) шлёт напоминание доплатить
    /// до полного доступа: уплаченное за пробный месяц засчитывается в grace-окне.
    /// Идемпотентность per-grant через <c>GrantId</c>. Каналы InApp + Telegram.
    /// </summary>
    TrialExpiryApproaching = 20,

    /// <summary>
    /// Нудж на вступление в Telegram-группу плана (#616) — recipient = сам пользователь.
    /// AccessService (on-grant handler + <c>TgJoinReminderSweeper</c>) шлёт <c>TgJoinReminderRequested</c>
    /// в три стадии (INITIAL / REMINDER_1 / REMINDER_2), пока юзер не вступил в чат.
    /// Каналы зависят от стадии: INITIAL → InApp + Telegram (без Email); REMINDER_1/REMINDER_2 →
    /// InApp + Telegram + Email. Telegram авто-фильтруется dispatcher'ом если TG не привязан —
    /// именно тогда срабатывает Email как единственный out-of-band канал. Идемпотентность
    /// per-(grant, stage) через <c>CorrelationIds.Combine(GrantId, stageGuid)</c>.
    /// </summary>
    TelegramJoinReminder = 21,

    /// <summary>
    /// Авто-обработка видео упала (#648) — recipient = владелец видео (автор). Платформа
    /// сама запустила транскрипцию + тайм-коды по готовности видео, но pipeline упал
    /// (например, STT-провайдер вернул ошибку). Автор кнопку не нажимал, поэтому узнаёт об
    /// этом из уведомления; дип-линк ведёт в редактор материала, где можно перезапустить
    /// вручную. Ручные (MANUAL) job'ы это уведомление НЕ шлют. Каналы InApp + Telegram.
    /// </summary>
    VideoAutoProcessingFailed = 22,

    /// <summary>
    /// Доступ по плану истёк (#687) — recipient = сам пользователь. AccessService (фоновый
    /// <c>ExpiredGrantsSweeper</c>) перевёл time-limited grant в EXPIRED по TTL и опубликовал
    /// <c>plan_grant.expired</c>. В отличие от pre-expiry напоминания (<see cref="TrialExpiryApproaching"/>),
    /// это уведомление приходит ПОСЛЕ потери доступа и зовёт продлить — при доплате зачтётся уже
    /// оплаченное. Каналы InApp + Telegram + Email (владелец явно хочет все три). Идемпотентность
    /// per-grant через <c>GrantId</c>. Deep-link на каталог планов (<c>/pricing</c>).
    /// </summary>
    AccessExpired = 23,

    /// <summary>
    /// Студент задал приватный вопрос автору по заданию ДО отправки решения (#693) — recipient =
    /// автор курса. Issue-scoped аналог <see cref="AuthorHelpRequested"/> (#383, submission-scoped):
    /// студент завис на чтении задания, сабмишена ещё нет. Telegram-тело несёт текст вопроса +
    /// t.me-контакт студента; клик ведёт на страницу задания. Каналы InApp + Telegram (без email).
    /// </summary>
    IssueAuthorQuestion = 24,

    /// <summary>
    /// «Вход теперь по почте» (#704, epic #696) — recipient = пользователь с GitHub-привязкой.
    /// GitHub-вход удалён по закону (149-ФЗ п.10 ст.8 + штрафы 199-ФЗ с 07.07.2026); аккаунт и
    /// привязка сохранены, вход — по OTP-коду на почту. One-shot admin-кампания
    /// (<c>/notifications/admin/campaigns/email-login-notice/*</c>). Каналы InApp + Email;
    /// email ФОРСИРОВАН (критичное уведомление об аккаунте — идёт даже при выключенном
    /// Email-канале и per-type opt-out'е, см. <c>NotificationTemplate.ForcedChannels</c>).
    /// Идемпотентность per-user через фиксированный correlation (campaign GUID × userId).
    /// </summary>
    EmailLoginNotice = 25,

    /// <summary>
    /// «Привяжите GitHub и Telegram» (#704, epic #696) — recipient = пользователь БЕЗ
    /// GitHub-привязки. Мягкий nudge: GitHub даёт орг-доступ к курсам и AI-review PR,
    /// Telegram — чаты и уведомления. One-shot admin-кампания
    /// (<c>/notifications/admin/campaigns/link-accounts-nudge/*</c>). Канал ТОЛЬКО InApp;
    /// deep-link /settings/integrations. Идемпотентность per-user (campaign GUID × userId).
    /// </summary>
    LinkAccountsNudge = 26,

    /// <summary>
    /// Студент задал вопрос reply'ем в своём GitHub-PR (#713, epic pr-dialogue) — recipient =
    /// автор курса. Обратный канал к AI-проверке: ARS услышал вопрос через webhook, сохранил
    /// <c>StudentPrMessage</c> и опубликовал <c>StudentPrQuestionAsked</c>. Telegram-тело несёт
    /// текст вопроса + t.me-контакт студента (если <c>StudentUserId</c> известен) + прямую ссылку
    /// на тред в PR. Клик по уведомлению ведёт на панель проверки <c>/author/review</c>, где автор
    /// увидит вопрос и ответит. Каналы InApp + Telegram (без email — как прочие author-review
    /// уведомления). Идемпотентность per-message через <c>StudentPrMessageId</c>.
    /// </summary>
    StudentPrQuestionAsked = 27,

    /// <summary>Успешное автопродление подписки — тихое InApp-подтверждение пользователю.</summary>
    SubscriptionRenewed = 28,

    /// <summary>
    /// Проблема автосписания: retry запланирован либо попытки исчерпаны. Конкретная стадия
    /// и даты приходят из AccessService; NotificationService не пересчитывает billing policy.
    /// </summary>
    SubscriptionRenewalProblem = 29,

    /// <summary>Пользователь отключил автопродление, сохранив доступ до конца оплаченного срока.</summary>
    SubscriptionRenewalCancelled = 30,
}