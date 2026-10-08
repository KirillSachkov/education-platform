using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Database;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Core.Templates.Rendering;
using NotificationService.Domain.Notifications;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>assignment_review.events / student_pr_question.asked</c> → автору курса (#713, epic pr-dialogue).
///
/// Студент задал вопрос reply'ем в своём GitHub-PR — обратный канал к AI-проверке. Событие
/// самодостаточно (несёт имя/логин студента, текст вопроса, ссылки), поэтому ECS-lookup не нужен —
/// клик ведёт на статичную панель проверки <c>/author/review</c>. Единственный внешний вызов —
/// AuthService для t.me-контакта студента, и только если <c>StudentUserId</c> известен (github-only
/// автор PR без платформенного аккаунта → просто без контакта). Зеркалит приём
/// <c>IssueSubmissionAuthorHelpRequestedHandler</c> (#575): raw Telegram-блоки с поэлементным
/// MarkdownV1-escape. Идемпотентность по <c>StudentPrMessageId</c> (ARS публикует один раз на
/// новое сообщение).
/// </summary>
public sealed class StudentPrQuestionAskedHandler
{
    private const int PREVIEW_MAX_LENGTH = 280;

    private readonly INotificationDispatcher _dispatcher;
    private readonly INotificationsRepository _notifications;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<StudentPrQuestionAskedHandler> _logger;

    public StudentPrQuestionAskedHandler(
        INotificationDispatcher dispatcher,
        INotificationsRepository notifications,
        IAuthServiceClient authClient,
        ILogger<StudentPrQuestionAskedHandler> logger)
    {
        _dispatcher = dispatcher;
        _notifications = notifications;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(StudentPrQuestionAsked evt, CancellationToken ct)
    {
        // Коалесинг пуш-спама (#713, security-MED): если у автора уже есть НЕПРОЧИТАННОЕ
        // уведомление о вопросе по ЭТОЙ ЖЕ сдаче — не плодим новый пуш (студент мог написать
        // серию комментов). Денорм-бейдж (ProgressService) и тред (ARS) всё равно показывают
        // все вопросы; теряется лишь дублирующее уведомление. Прочитал автор → следующий
        // вопрос создаст новое. Идемпотентность на retry держит correlation=StudentPrMessageId.
        if (await _notifications.HasUnreadOfTypeForSubmissionAsync(
                evt.AuthorId, NotificationType.StudentPrQuestionAsked, evt.SubmissionId, ct))
        {
            _logger.LogDebug(
                "Coalescing StudentPrQuestionAsked for author {AuthorId}: unread notification for submission " +
                "{SubmissionId} already exists — skipping duplicate push.",
                evt.AuthorId, evt.SubmissionId);
            return;
        }

        // Кто спрашивает — из самого события (StudentName / github-login), без lookup'а.
        string studentName = FirstNonBlank(evt.StudentName, evt.StudentGithubLogin) ?? "студент";

        // t.me-контакт — только если студент известен платформе; github-only автор PR → без контакта.
        string? telegramUsername = evt.StudentUserId is Guid studentUserId
            ? (await ResolveStudentAsync(studentUserId, ct))?.TelegramUsername
            : null;

        string pullRequest = $"{evt.RepoFullName}#{evt.PullNumber}";
        string threadUrl = FirstNonBlank(evt.CommentUrl, evt.PullRequestUrl) ?? string.Empty;

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.StudentPrQuestionAsked,
            recipientUserId: evt.AuthorId,
            correlationId: evt.StudentPrMessageId,
            // Raw-блоки: текст вопроса (всегда есть) + t.me-контакт студента + прямая ссылка на
            // тред в PR — собраны здесь с поэлементным MarkdownV1-escape (вопрос / handle —
            // user-derived; URL — GitHub-адрес, кладём в inline-link где entities не парсятся).
            args: TemplateArgs.Of(
                    ("studentName", studentName),
                    ("pullRequest", pullRequest),
                    ("questionPreview", Truncate(evt.Body)))
                .WithRaw("questionTextTg", BuildQuestionBlock(evt.Body))
                .WithRaw("studentContactTg", BuildTelegramContactBlock(telegramUsername))
                .WithRaw("prLinkTg", BuildPrLinkBlock(threadUrl)),
            payload: new
            {
                submissionId = evt.SubmissionId,
                issueId = evt.IssueId,
                aiReviewId = evt.AiReviewId,
                studentPrMessageId = evt.StudentPrMessageId,
                commentUrl = evt.CommentUrl,
                pullRequestUrl = evt.PullRequestUrl,
            });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private async Task<AuthUserLookupDto?> ResolveStudentAsync(Guid studentUserId, CancellationToken ct)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> lookup =
            await _authClient.GetUsersByIdsAsync([studentUserId], ct);
        if (lookup.IsSuccess && lookup.Value is { Count: > 0 })
            return lookup.Value[0];

        _logger.LogWarning(
            "Auth lookup failed for student {UserId}: {Error}. Notifying author without t.me contact.",
            studentUserId, lookup.ErrorText());
        return null;
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return null;
    }

    private static string Truncate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return string.Empty;

        string trimmed = body.Trim();
        return trimmed.Length <= PREVIEW_MAX_LENGTH
            ? trimmed
            : string.Concat(trimmed.AsSpan(0, PREVIEW_MAX_LENGTH), "…");
    }

    /// <summary>
    ///     Telegram-блок с текстом вопроса студента. Вопрос обязателен, но защищаемся от пустого.
    ///     Raw-значение → escape'им целиком сами.
    /// </summary>
    private static string BuildQuestionBlock(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        return $"\n\n💬 _Вопрос:_\n{TelegramRenderer.EscapeMarkdownV1(message.Trim())}";
    }

    /// <summary>
    ///     Telegram-блок с контактом студента (зеркало #575). Пустая строка, если Telegram не
    ///     привязан или студент не известен платформе. Валидный @handle → кликабельная t.me-ссылка;
    ///     иначе — plain-текст. Raw-значение → handle escape'им поэлементно сами.
    /// </summary>
    private static string BuildTelegramContactBlock(string? telegramHandle)
    {
        if (string.IsNullOrWhiteSpace(telegramHandle))
            return string.Empty;

        string handle = telegramHandle.Trim().TrimStart('@');
        if (handle.Length == 0)
            return string.Empty;

        bool looksLikeTgUsername = handle.Length is >= 4 and <= 32
            && handle.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

        string escLabel = TelegramRenderer.EscapeMarkdownV1(handle);
        return looksLikeTgUsername
            ? $"\n\n📱 Telegram: [@{escLabel}](https://t.me/{handle})"
            : $"\n\n📱 Telegram: @{escLabel}";
    }

    /// <summary>
    ///     Telegram-блок с прямой ссылкой на тред в PR. URL — GitHub-адрес (html_url коммента /
    ///     PR), внутри inline-link'а <c>[label](url)</c> MarkdownV1-entities не парсятся, поэтому
    ///     URL кладём как есть. Пустая строка, если ссылка отсутствует / не http(s).
    /// </summary>
    private static string BuildPrLinkBlock(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return string.Empty;

        string trimmed = url.Trim();
        bool isHttp = trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        return isHttp ? $"\n🔗 [Ответить в PR]({trimmed})" : string.Empty;
    }
}
