using NotificationService.Core.Templates.Parts;
using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Templates.Catalog;

/// <summary>
/// Каталог шаблонов уведомлений / Notification templates catalog.
///
/// Каждый <see cref="NotificationTemplate"/> = композиция per-channel parts.
/// Соглашение: <see cref="InAppTemplate"/> — обязательна (минимализм, plain text);
/// <see cref="TelegramTemplate"/> и <see cref="EmailTemplate"/> — опциональны (если канал
/// не имеет смысла для уведомления — просто не передаём part).
///
/// Добавление нового уведомления = (1) поле в <see cref="NotificationType"/>,
/// (2) запись тут, (3) handler в <c>Core/Notifications/Handlers/</c>.
/// </summary>
public static class NotificationTemplates
{
    /// <summary>
    /// Приветствие при регистрации / Welcome on sign-up.
    /// Аргументы: displayName, frontendUrl.
    /// </summary>
    public static readonly NotificationTemplate Welcome = new(
        id: "welcome",
        type: NotificationType.Welcome,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Email,
        inApp: new(
            Title: "Добро пожаловать!",
            Body: "Рады видеть вас на платформе, {displayName}."),
        telegram: new(
            Body:
            "👋 Привет, *{displayName}*!\n\nРады видеть вас на Sachkov Learn. Открывайте каталог и выбирайте курс.\n\n[Открыть платформу]({openUrl})"),
        email: new(
            Subject: "Добро пожаловать на Sachkov Learn",
            HtmlBodyResource: "welcome.html",
            PlainText:
            "Привет, {displayName}!\n\n" +
            "Рады видеть вас на Sachkov Learn. Открывайте каталог и выбирайте курс: {frontendUrl}"));

    /// <summary>
    /// Пользователь записан на курс / User enrolled into a course.
    /// Аргументы: courseTitle, courseUrl.
    /// Каналы: InApp + Telegram + Email — зачисление организационное, дублируется во все каналы.
    /// </summary>
    public static readonly NotificationTemplate CourseEnrolled = new(
        id: "course.enrolled",
        type: NotificationType.CourseEnrolled,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email,
        inApp: new(
            Title: "Запись на курс",
            Body: "Вы записаны на «{courseTitle}». Можно приступать к первому модулю."),
        telegram: new(
            Body: "✅ Запись на курс *{courseTitle}*\n\nДоступ к материалам уже открыт.\n\n[Открыть курс]({openUrl})"),
        email: new(
            Subject: "Вы записаны на курс «{courseTitle}»",
            HtmlBodyResource: "course-enrolled.html"));

    /// <summary>
    /// Новый материал в курсе / New material in a course.
    /// Аргументы: courseTitle, materialTitle, materialUrl.
    /// </summary>
    public static readonly NotificationTemplate MaterialPublished = new(
        id: "material.published",
        type: NotificationType.MaterialPublished,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Новый материал",
            Body: "«{materialTitle}» — в курсе «{courseTitle}»."),
        telegram: new(
            Body: "📘 Новый материал в курсе *{courseTitle}*\n\n«{materialTitle}» — уже доступен.\n\n[Открыть материал]({openUrl})"),
        email: new(
            Subject: "Новый материал в курсе «{courseTitle}»",
            HtmlBodyResource: "material-published.html"));

    /// <summary>
    /// Сабмит одобрен / Submission approved.
    /// Аргументы: issueTitle, reviewerComment (может быть пустым), issueUrl.
    /// </summary>
    public static readonly NotificationTemplate IssueSubmissionApproved = new(
        id: "issue_submission.approved",
        type: NotificationType.IssueSubmissionApproved,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Решение принято",
            Body: "«{issueTitle}» — ваше решение принято. {reviewerComment}"),
        telegram: new(
            Body: "✅ *Решение принято*\n\nЗадача «{issueTitle}» — принято.\n\n{reviewerComment}\n\n[Перейти к задаче]({openUrl})"),
        email: new(
            Subject: "Решение принято — «{issueTitle}»",
            HtmlBodyResource: "issue-approved.html"));

    /// <summary>
    /// Запрошены изменения по сабмиту / Submission changes requested.
    /// Аргументы: issueTitle, reviewerComment, issueUrl.
    /// </summary>
    public static readonly NotificationTemplate IssueSubmissionChangesRequested = new(
        id: "issue_submission.changes_requested",
        type: NotificationType.IssueSubmissionChangesRequested,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Нужны правки",
            Body: "«{issueTitle}» — запрошены изменения. {reviewerComment}"),
        telegram: new(
            Body: "✏️ *Нужны правки*\n\nЗадача «{issueTitle}»:\n\n{reviewerComment}\n\n[Перейти к задаче]({openUrl})"),
        email: new(
            Subject: "Нужны правки — «{issueTitle}»",
            HtmlBodyResource: "issue-changes-requested.html"));

    /// <summary>
    /// Автору пришло новое решение задачи на ревью / Submission awaiting review by the author.
    /// Аргументы: issueTitle, studentName, reviewUrl.
    /// </summary>
    public static readonly NotificationTemplate IssueSubmissionAwaitingReview = new(
        id: "issue_submission.awaiting_review",
        type: NotificationType.IssueSubmissionAwaitingReview,
        defaultChannels: NotificationChannel.InApp,
        inApp: new(
            Title: "Новое решение на ревью",
            Body: "{studentName} → «{issueTitle}»"),
        telegram: new(
            Body: "📥 *Новое решение на ревью*\n\n{studentName} отправил(а) решение по задаче «{issueTitle}».\n\n[Открыть на проверку]({openUrl})"),
        email: new(
            Subject: "Новое решение на ревью — «{issueTitle}»",
            HtmlBodyResource: "issue-awaiting-review.html"));

    /// <summary>
    /// Telegram успешно привязан / Telegram successfully linked.
    /// Аргументы: telegramUsername, frontendUrl.
    /// </summary>
    public static readonly NotificationTemplate TelegramLinked = new(
        id: "telegram.linked",
        type: NotificationType.TelegramLinked,
        defaultChannels: NotificationChannel.InApp,
        inApp: new(
            Title: "Telegram привязан",
            Body: "Аккаунт @{telegramUsername} успешно привязан."),
        telegram: new(
            Body:
            "🔗 *Telegram привязан*\n\nТеперь уведомления будут приходить сюда. Управляйте каналами в настройках профиля.\n\n[Открыть настройки]({openUrl})"),
        email: new(
            Subject: "Telegram привязан",
            HtmlBodyResource: "telegram-linked.html"));

    /// <summary>
    /// Кто-то ответил на ваш комментарий / Someone replied to your comment.
    /// Аргументы: authorName, preview.
    /// Каналы: InApp + Telegram (без Email — продуктовое решение, чтобы не спамить
    /// почтовый ящик ответами; пользователь увидит на сайте и в боте, если привязан).
    /// </summary>
    public static readonly NotificationTemplate CommentReplied = new(
        id: "comment.replied",
        type: NotificationType.CommentReplied,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Ответ от {authorName}",
            Body: "{preview}"),
        telegram: new(
            Body: "💬 *Ответ от {authorName}*\n\n{preview}\n\n[Перейти к комментарию]({openUrl})"));

    /// <summary>
    /// Новый комментарий к вашему материалу/задаче / New comment on your content.
    /// Аргументы: authorName, preview.
    /// Каналы: InApp + Telegram. Email отключён, чтобы не спамить почту per-comment'ом —
    /// шумные авторы могут отписаться от типа целиком через UserNotificationTypeOptOut
    /// или выключить Telegram-флаг в настройках.
    /// </summary>
    public static readonly NotificationTemplate CommentOnOwnContent = new(
        id: "comment.on_own_content",
        type: NotificationType.CommentOnOwnContent,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Комментарий от {authorName}",
            Body: "{preview}"),
        telegram: new(
            Body: "💬 *Комментарий от {authorName}*\n\n{preview}\n\n[Перейти к комментарию]({openUrl})"));

    /// <summary>
    /// Новое задание в курсе / New issue in a course.
    /// Аргументы: courseTitle, issueTitle, issueUrl.
    /// Каналы: InApp + Telegram (без Email — учёба, не email-спам).
    /// </summary>
    public static readonly NotificationTemplate IssuePublished = new(
        id: "issue.published",
        type: NotificationType.IssuePublished,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Новое задание",
            Body: "«{issueTitle}» — в курсе «{courseTitle}»."),
        telegram: new(
            Body: "📝 Новое задание в курсе *{courseTitle}*\n\n«{issueTitle}» — уже доступно.\n\n[Открыть задание]({openUrl})"));

    /// <summary>
    /// Анонс от автора / Author announcement (broadcast).
    /// Текст передаётся полностью из event — placeholders передают title/body напрямую.
    /// Аргументы: announcementTitle, announcementBody.
    /// </summary>
    public static readonly NotificationTemplate AuthorAnnouncement = new(
        id: "author.announcement",
        type: NotificationType.AuthorAnnouncement,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "{announcementTitle}",
            Body: "{announcementBody}"),
        telegram: new(
            Body: "📣 *{announcementTitle}*\n\n{announcementBody}\n\n[Подробнее]({openUrl})"),
        email: new(
            Subject: "{announcementTitle}",
            HtmlBodyResource: "author-announcement.html"));

    /// <summary>
    /// Студент нажал «Позвать автора» по своему решению (#383) → автору курса.
    /// AI-ассистент не справился / нужна живая помощь — студент явно подключает автора.
    /// Аргументы: studentName, issueTitle, openUrl; raw-блоки studentContactTg (кликабельная
    /// t.me-ссылка студента, #575) + helpMessageTg (текст «в чём нужна помощь», #575) —
    /// собираются хендлером с поэлементным MarkdownV1-escape.
    /// Каналы: InApp + Telegram (как и прочие author-review уведомления — без email-спама).
    /// </summary>
    public static readonly NotificationTemplate AuthorHelpRequested = new(
        id: "issue_submission.author_help_requested",
        type: NotificationType.AuthorHelpRequested,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Студенту нужна помощь",
            Body: "{studentName} зовёт вас на «{issueTitle}»."),
        telegram: new(
            Body: "🆘 *Студенту нужна помощь*\n\n{studentName} просит вас подключиться к задаче «{issueTitle}».{studentContactTg}{helpMessageTg}\n\n[Открыть на проверку]({openUrl})"));

    /// <summary>
    /// Студент задал приватный вопрос автору по заданию ДО отправки решения (#693) → автору курса.
    /// Issue-scoped аналог <see cref="AuthorHelpRequested"/> (#383) — сабмишена ещё нет, студент
    /// завис на чтении задания. Аргументы: studentName, issueTitle, openUrl; raw-блоки
    /// studentContactTg (кликабельная t.me-ссылка студента) + questionTextTg (текст вопроса) —
    /// собираются хендлером с поэлементным MarkdownV1-escape. Тело вопроса видно прямо в Telegram.
    /// Каналы: InApp + Telegram (как и прочие author-уведомления — без email-спама).
    /// </summary>
    public static readonly NotificationTemplate IssueAuthorQuestion = new(
        id: "issue.author_question",
        type: NotificationType.IssueAuthorQuestion,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Вопрос по заданию",
            Body: "{studentName} спрашивает по «{issueTitle}»."),
        telegram: new(
            Body: "❓ *Вопрос по заданию*\n\n{studentName} спрашивает по «{issueTitle}».{questionTextTg}{studentContactTg}\n\n[Открыть задание]({openUrl})"));

    /// <summary>
    /// Большой PR (#546): авто-ран AI-проверки пропущен (reviewable diff выше hard-cap'ов
    /// ARS) → автору курса. Ручной «Перепроверить» на странице ревью снимает cap и
    /// проверяет PR целиком по частям.
    /// Аргументы: studentName, issueTitle, pullRequest («owner/repo#N»).
    /// Каналы: InApp + Telegram (как и прочие author-review уведомления — без email-спама).
    /// </summary>
    public static readonly NotificationTemplate AiReviewOversizedSkipped = new(
        id: "ai_review.oversized_skipped",
        type: NotificationType.AiReviewOversizedSkipped,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Большой PR — запустите AI-проверку вручную",
            Body: "{studentName} сдал(а) «{issueTitle}» ({pullRequest}). PR слишком большой для авто-проверки — запустите её вручную кнопкой «Перепроверить»."),
        telegram: new(
            Body: "📦 *Большой PR — авто-проверка пропущена*\n\n{studentName} сдал(а) «{issueTitle}» ({pullRequest}). Запустите AI-проверку вручную — она разобьёт PR на части и проверит целиком.\n\n[Открыть на проверку]({openUrl})"));

    /// <summary>
    /// Студент задал вопрос reply'ем в своём GitHub-PR (#713, epic pr-dialogue) → автору курса.
    /// Обратный канал к AI-проверке: студент спрашивает прямо в треде PR, автор отвечает на панели
    /// проверки <c>/author/review</c>. Аргументы: studentName, pullRequest («owner/repo#N»),
    /// questionPreview (усечённый текст вопроса для InApp); raw-блоки questionTextTg (полный текст
    /// вопроса), studentContactTg (t.me-ссылка студента, если Telegram привязан) и prLinkTg (прямая
    /// ссылка на тред в PR) — собираются хендлером с поэлементным MarkdownV1-escape.
    /// Каналы: InApp + Telegram (как и прочие author-review уведомления — без email-спама).
    /// </summary>
    public static readonly NotificationTemplate StudentPrQuestionAsked = new(
        id: "assignment_review.student_pr_question",
        type: NotificationType.StudentPrQuestionAsked,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Вопрос по PR от студента",
            Body: "{studentName} задал(а) вопрос в PR ({pullRequest}): {questionPreview}"),
        telegram: new(
            Body: "💬 *Вопрос по PR от студента*\n\n{studentName} задал(а) вопрос в PR ({pullRequest}).{questionTextTg}{studentContactTg}\n\n[Открыть на проверку]({openUrl}){prLinkTg}"));

    /// <summary>
    /// Доступ открыт по плану (redeem invite, admin grant, PURCHASE, Telegram-бот, GitHub-org).
    /// Заменяет N per-course CourseEnrolled-уведомлений когда юзеру открывается несколько
    /// курсов одним plan-grant'ом.
    /// Аргументы: planSummary — «Открыт доступ к интенсиву «X»» (offer-aware, см.
    /// PlanGrantReceivedHandler). Тело короткое (#485): onboarding-чеклист убран — по шагам
    /// ведёт onboarding-визард (#68) на платформе, а клик deep-link'ает в курс / на /home.
    /// </summary>
    public static readonly NotificationTemplate PlanGrantReceived = new(
        id: "plan_grant.received",
        type: NotificationType.PlanGrantReceived,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email,
        inApp: new(
            Title: "Доступ открыт",
            Body: "{planSummary}. Нажмите, чтобы начать учиться."),
        telegram: new(
            Body: "🎉 *Доступ открыт*\n\n{planSummary}.\n\n[Начать учиться]({openUrl})"),
        email: new(
            Subject: "Доступ открыт",
            HtmlBodyResource: "plan-grant-received.html"));

    /// <summary>
    /// Автору пришёл новый участник плана (#428) — recipient = автор плана.
    /// Покрывает все «человеческие» источники гранта (покупка, инвайт, admin-grant, github-org,
    /// trial); backfill (Source=MIGRATION) и self-grant (автор == получатель) отсекаются в хендлере.
    /// Аргументы: buyerLine (имя + платформенный ник без @), buyerEmail, planName (название плана/продукта),
    /// accessSummary (объём доступа), accessTerm (срок — «Навсегда» / «На месяц …», #632), sourceLabel (способ).
    /// «План/курс», «Доступ» и «Срок» — разные сущности (#445, #632).
    /// Каналы: InApp + Telegram (без Email — уведомление автора о продаже идёт только in-app/Telegram,
    /// #615: email убран чтобы не показывать buyerLine с @-prefix'ом в письме и следовать продуктовому правилу).
    /// </summary>
    public static readonly NotificationTemplate PlanGrantAuthorSale = new(
        id: "plan_grant.author_sale",
        type: NotificationType.PlanGrantAuthorSale,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Новый доступ к плану",
            Body: "{buyerLine} · {buyerEmail}\nПлан/курс: {planName}\nДоступ: {accessSummary}\nСрок: {accessTerm}\nСпособ: {sourceLabel}"),
        telegram: new(
            Body:
            "🛒 *Новый доступ к плану*\n\n{buyerLine}\n✉️ {buyerEmail}\n\nПлан/курс: *{planName}*\nДоступ: {accessSummary}\nСрок: *{accessTerm}*\nСпособ: {sourceLabel}\n\n[Открыть продажи]({openUrl})"));

    /// <summary>
    /// Еженедельный платформенный дайджест «что нового за неделю» (#468, глобальный для всех
    /// пользователей — #532): опубликованные за 7 дней материалы и курсы из ECS.
    /// Генерируется фоновым <c>WeeklyDigestService</c> (без новых RabbitMQ-событий).
    /// Аргументы: digestSummary («N новых материалов, M новых курсов»),
    /// digestItems (plain-строки для InApp/plain-text), плюс raw-аргументы (#532):
    /// digestItemsTg (MarkdownV1-ссылки на элементы) и digestItemsHtml (ul-список ссылок) —
    /// собираются runner'ом с поэлементным экранированием заголовков.
    /// </summary>
    public static readonly NotificationTemplate WeeklyDigest = new(
        id: "digest.weekly",
        type: NotificationType.WeeklyDigest,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram,
        inApp: new(
            Title: "Что нового за неделю",
            Body: "{digestSummary}.\n\n{digestItems}"),
        telegram: new(
            Body:
            "🗞 *Что нового за неделю*\n\n{digestSummary}.\n\n{digestItemsTg}\n\n[Открыть платформу]({openUrl})"),
        email: new(
            Subject: "Что нового за неделю на Sachkov Learn",
            HtmlBodyResource: "weekly-digest.html",
            PlainText:
            "Что нового за неделю: {digestSummary}.\n\n{digestItems}\n\nОткрыть платформу: {openUrl}"));

    /// <summary>
    /// Месячный доступ скоро истекает (#580) → самому пользователю. CTA — оформить полный
    /// доступ навсегда (уплаченное за месяц в зачёте). Клик ведёт на каталог планов
    /// (/pricing), где оформляется апгрейд.
    /// Аргументы: planName (название плана, напр. «Полный доступ на месяц»), expiresAt
    /// (дата истечения dd.MM.yyyy, форматируется хендлером), openUrl.
    /// Каналы: InApp + Telegram + Email (#687 — владелец захотел дублировать pre-expiry
    /// напоминание ещё и письмом, чтобы покупатель точно увидел до потери доступа).
    /// </summary>
    public static readonly NotificationTemplate TrialExpiryApproaching = new(
        id: "trial.expiry_approaching",
        type: NotificationType.TrialExpiryApproaching,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email,
        inApp: new(
            Title: "Доступ скоро закончится",
            Body: "Доступ по плану «{planName}» истекает {expiresAt}. Оформи полный доступ к .NET Fullstack — уплаченное за месяц пойдёт в зачёт."),
        telegram: new(
            Body: "⏳ *Доступ скоро закончится*\n\nДоступ по плану «{planName}» истекает {expiresAt}. Оформи полный доступ к .NET Fullstack — уплаченное за месяц пойдёт в зачёт.\n\n[Оформить полный доступ .NET Fullstack]({openUrl})"),
        email: new(
            Subject: "Доступ по плану «{planName}» скоро закончится",
            HtmlBodyResource: "trial-expiry-approaching.html",
            PlainText:
            "Доступ по плану «{planName}» истекает {expiresAt}.\n\n" +
            "Оформи полный доступ к .NET Fullstack — уплаченное за месяц пойдёт в зачёт: {openUrl}"));

    /// <summary>
    /// Доступ по плану истёк (#687) → самому пользователю. AccessService (фоновый
    /// <c>ExpiredGrantsSweeper</c>) перевёл time-limited grant в EXPIRED по TTL. В отличие от
    /// pre-expiry <see cref="TrialExpiryApproaching"/>, приходит ПОСЛЕ потери доступа и зовёт
    /// продлить — при доплате зачтётся уже оплаченное. Клик ведёт в каталог планов (/pricing).
    /// Событие <c>PlanGrantExpired</c> не несёт <c>PlanName</c>, поэтому хендлер строит
    /// <c>accessSummary</c> из <c>PlanTier</c> (как <c>PlanGrantReceivedHandler</c>).
    /// Аргументы: accessSummary (что именно истекло), openUrl.
    /// Каналы: InApp + Telegram + Email (владелец явно хочет все три).
    /// </summary>
    public static readonly NotificationTemplate AccessExpired = new(
        id: "access.expired",
        type: NotificationType.AccessExpired,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email,
        inApp: new(
            Title: "Доступ закончился",
            Body: "{accessSummary} истёк. Продлите доступ — при доплате зачтётся уже оплаченное."),
        telegram: new(
            Body: "⌛ *Доступ закончился*\n\n{accessSummary} истёк. Продлите доступ — при доплате зачтётся уже оплаченное.\n\n[Продлить доступ]({openUrl})"),
        email: new(
            Subject: "Доступ закончился",
            HtmlBodyResource: "access-expired.html",
            PlainText:
            "{accessSummary} истёк.\n\n" +
            "Продлите доступ — при доплате зачтётся уже оплаченное: {openUrl}"));

    /// <summary>
    /// «Вход теперь по почте» (#704, epic #696) — one-shot кампания пользователям с
    /// GitHub-привязкой: GitHub-вход удалён по закону, вход — по OTP-коду на почту.
    /// Аргументы: email (login-почта получателя, per-user), frontendUrl; raw: loginUrl
    /// (ссылка на /login), supportTg (контакт поддержки в Telegram — тот же
    /// t.me/sachkov_blog, что в остальных email-шаблонах).
    /// Каналы: InApp + Email; Email ФОРСИРОВАН (<c>forcedChannels</c>) — критичное
    /// уведомление об аккаунте, идёт поверх выключенного Email-канала и opt-out'а.
    /// </summary>
    public static readonly NotificationTemplate EmailLoginNotice = new(
        id: "auth.email_login_notice",
        type: NotificationType.EmailLoginNotice,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Email,
        forcedChannels: NotificationChannel.Email,
        inApp: new(
            Title: "Вход теперь по почте",
            Body: "Кнопку «Войти через GitHub» отключили — так требует закон. Входите по коду на почту {email}. Аккаунт тот же, привязка GitHub и доступы работают как раньше."),
        email: new(
            Subject: "Вход через GitHub отключён — теперь вход по этой почте",
            HtmlBodyResource: "email-login-notice.html",
            PlainText:
            "Привет!\n\n" +
            "С 7 июля вход на российские сайты через иностранные сервисы запрещён законом.\n" +
            "Поэтому мы убрали кнопку «Войти через GitHub».\n\n" +
            "Ваш аккаунт и весь прогресс на месте. Теперь входите по почте:\n\n" +
            "1. Откройте страницу входа: {loginUrl}\n" +
            "2. Введите эту почту: {email}\n" +
            "3. Получите код и войдите — это тот же аккаунт.\n\n" +
            "Привязка GitHub осталась и работает как раньше: доступ к курсам по организации\n" +
            "и проверка PR никуда не делись.\n\n" +
            "Если к этой почте нет доступа или что-то не получается — напишите нам\n" +
            "в Telegram: {supportTg}."));

    /// <summary>
    /// «Привяжите GitHub и Telegram» (#704, epic #696) — one-shot nudge пользователям БЕЗ
    /// GitHub-привязки: ценность привязок (орг-доступ к курсам, AI-review PR, чаты и
    /// уведомления). Без аргументов (текст статичный).
    /// Каналы: ТОЛЬКО InApp (мягкий продуктовый nudge — не письмо и не мессенджер-шум);
    /// deep-link /settings/integrations.
    /// </summary>
    public static readonly NotificationTemplate LinkAccountsNudge = new(
        id: "auth.link_accounts_nudge",
        type: NotificationType.LinkAccountsNudge,
        defaultChannels: NotificationChannel.InApp,
        inApp: new(
            Title: "Привяжите GitHub и Telegram",
            Body: "GitHub даёт автоматический доступ к курсам вашей организации и проверку PR. Telegram — чаты курсов и уведомления. Привязка занимает минуту."));

    /// <summary>
    /// Нудж на вступление в Telegram-группу плана (#616) → самому пользователю. Зовём в чат
    /// курса, пока пользователь не вступил. Per-стадийный набор каналов выбирает хендлер через
    /// <c>channelsOverride</c>: INITIAL → InApp + Telegram (без Email); REMINDER_1/REMINDER_2 →
    /// InApp + Telegram + Email. Если Telegram не привязан, dispatcher отфильтрует TG-канал, и на
    /// reminder-стадиях останется Email — единственный out-of-band способ достучаться.
    /// Аргументы: planName (имя плана/курса), openUrl (proxy-ссылка → флоу привязки + вступления).
    /// </summary>
    public static readonly NotificationTemplate TelegramJoinReminder = new(
        id: "telegram.join_reminder",
        type: NotificationType.TelegramJoinReminder,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email,
        inApp: new(
            Title: "Вступай в Telegram-группу",
            Body: "У тебя есть доступ к чату курса «{planName}». Зайди и присоединяйся к ученикам."),
        telegram: new(
            Body: "💬 *Вступай в Telegram-группу*\n\nУ тебя есть доступ к чату курса «{planName}» — там общаются ученики и автор. Заходи и присоединяйся! 👇\n\n[Вступить в группу]({openUrl})"),
        email: new(
            Subject: "Вступай в Telegram-группу курса «{planName}»",
            HtmlBodyResource: "telegram-join.html",
            PlainText:
            "У тебя есть доступ к чату курса «{planName}».\n\n" +
            "Там общаются ученики и автор — заходи, знакомься и задавай вопросы. " +
            "Привяжи Telegram и вступи в группу: {openUrl}"));

    public static readonly NotificationTemplate SubscriptionRenewed = new(
        id: "subscription.renewed",
        type: NotificationType.SubscriptionRenewed,
        defaultChannels: NotificationChannel.InApp,
        inApp: new(
            Title: "Подписка продлена",
            Body: "Оплата прошла. Доступ продлён до {expiresAt}. {nextChargeLine}"));

    public static readonly NotificationTemplate SubscriptionRenewalRetryScheduled = new(
        id: "subscription.renewal_retry_scheduled",
        type: NotificationType.SubscriptionRenewalProblem,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email,
        inApp: new(
            Title: "Не удалось продлить подписку",
            Body: "Списание не прошло. Повторим попытку {nextRetryAt}. Проверьте карту и баланс. Доступ сохранён до {graceEndsAt}."),
        telegram: new(
            Body: "⚠️ *Не удалось продлить подписку*\n\nСписание не прошло. Повторим попытку {nextRetryAt}. Проверьте карту и баланс. Доступ сохранён до {graceEndsAt}.\n\n[Открыть «Мои планы»]({openUrl})"),
        email: new(
            Subject: "Не удалось продлить подписку",
            HtmlBodyResource: "subscription-renewal-retry.html",
            PlainText:
            "Списание не прошло. Повторим попытку {nextRetryAt}.\n\n" +
            "Проверьте карту и баланс. Доступ сохранён до {graceEndsAt}.\n\n" +
            "Открыть «Мои планы»: {openUrl}"));

    public static readonly NotificationTemplate SubscriptionRenewalTerminalFailure = new(
        id: "subscription.renewal_terminal_failure",
        type: NotificationType.SubscriptionRenewalProblem,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email,
        inApp: new(
            Title: "Подписка не продлена",
            Body: "Все попытки списания завершились неуспешно. Автопродление остановлено. Доступ сохранён до {graceEndsAt}. Откройте «Мои планы», чтобы проверить оплату и возобновить подписку."),
        telegram: new(
            Body: "⛔ *Подписка не продлена*\n\nВсе попытки списания завершились неуспешно. Автопродление остановлено. Доступ сохранён до {graceEndsAt}.\n\n[Проверить оплату в «Моих планах»]({openUrl})"),
        email: new(
            Subject: "Подписка не продлена — проверьте оплату",
            HtmlBodyResource: "subscription-renewal-terminal.html",
            PlainText:
            "Все попытки списания завершились неуспешно. Автопродление остановлено.\n\n" +
            "Доступ сохранён до {graceEndsAt}. Откройте «Мои планы», чтобы проверить оплату " +
            "и возобновить подписку: {openUrl}"));

    public static readonly NotificationTemplate SubscriptionRenewalCancelled = new(
        id: "subscription.renewal_cancelled",
        type: NotificationType.SubscriptionRenewalCancelled,
        defaultChannels: NotificationChannel.InApp | NotificationChannel.Email,
        inApp: new(
            Title: "Автопродление отключено",
            Body: "Новых списаний не будет. Доступ сохранится до {accessEndsAt}. Возобновить подписку можно в разделе «Мои планы»."),
        email: new(
            Subject: "Автопродление подписки отключено",
            HtmlBodyResource: "subscription-renewal-cancelled.html",
            PlainText:
            "Новых списаний не будет. Доступ сохранится до {accessEndsAt}.\n\n" +
            "Возобновить подписку можно в разделе «Мои планы»: {openUrl}"));
}