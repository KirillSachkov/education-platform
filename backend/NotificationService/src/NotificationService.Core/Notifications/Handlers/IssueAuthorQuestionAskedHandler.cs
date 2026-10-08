using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.SearchLookup;
using Microsoft.Extensions.Logging;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Core.Templates.Rendering;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using SharedKernel;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>progress.events / issue.author_question_asked</c> → автору курса (#693).
///
/// Студент задал приватный вопрос по заданию ДО отправки решения (issue-scoped аналог
/// submission-scoped «Позвать автора» #383 — сабмишена ещё нет). Резолвит issue title +
/// course-slug из ECS (один lookup) и student display name из AuthService параллельно; на
/// failure любого — graceful fallback. Линк ведёт на страницу задания (через
/// <c>PlatformLinkBuilder</c>: <c>/courses/{slug}/issues/{id}</c> ⇒ fallback <c>/home</c>).
/// </summary>
public sealed class IssueAuthorQuestionAskedHandler
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<IssueAuthorQuestionAskedHandler> _logger;

    public IssueAuthorQuestionAskedHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<IssueAuthorQuestionAskedHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(IssueAuthorQuestionAsked evt, CancellationToken ct)
    {
        // Параллельный fetch — оба вызова cached, но на miss идут в HTTP.
        Task<IssueSearchLookupDto?> issueTask = ResolveIssueAsync(evt.IssueId, ct);
        Task<AuthUserLookupDto?> studentTask = ResolveStudentAsync(evt.StudentUserId, ct);
        await Task.WhenAll(issueTask, studentTask);
        IssueSearchLookupDto? issue = await issueTask;
        AuthUserLookupDto? student = await studentTask;

        string issueTitle = issue?.Title ?? "задача";
        string? courseSlug = issue?.CourseSlug;
        string studentName = student?.Name ?? student?.Username ?? "студент";

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.IssueAuthorQuestion,
            recipientUserId: evt.AuthorId,
            correlationId: evt.QuestionId,
            // Raw-блоки: текст вопроса (всегда есть, #693) + t.me-контакт студента собраны здесь
            // с поэлементным MarkdownV1-escape (вопрос / handle — user-derived).
            args: TemplateArgs.Of(
                    ("issueTitle", issueTitle),
                    ("studentName", studentName))
                .WithRaw("questionTextTg", BuildQuestionBlock(evt.Message))
                .WithRaw("studentContactTg", BuildTelegramContactBlock(student?.TelegramUsername)),
            payload: new { issueId = evt.IssueId, courseId = evt.CourseId, courseSlug });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private async Task<IssueSearchLookupDto?> ResolveIssueAsync(Guid issueId, CancellationToken ct)
    {
        Result<IssueSearchLookupDto, Error> lookup = await _ecsClient.GetIssueSearchLookupAsync(issueId, ct);
        if (lookup.IsSuccess && lookup.Value is not null)
            return lookup.Value;

        _logger.LogWarning(
            "ECS lookup failed for issue {IssueId}: {Error}. Using fallback.",
            issueId, lookup.ErrorText());
        return null;
    }

    private async Task<AuthUserLookupDto?> ResolveStudentAsync(Guid studentUserId, CancellationToken ct)
    {
        Result<IReadOnlyList<AuthUserLookupDto>, Error> lookup =
            await _authClient.GetUsersByIdsAsync([studentUserId], ct);
        if (lookup.IsSuccess && lookup.Value is { Count: > 0 })
            return lookup.Value[0];

        _logger.LogWarning(
            "Auth lookup failed for student {UserId}: {Error}. Using fallback.",
            studentUserId, lookup.ErrorText());
        return null;
    }

    /// <summary>
    ///     Telegram-блок с текстом вопроса студента (#693). Вопрос обязателен, но защищаемся от
    ///     пустого на всякий случай. Raw-значение → escape'им целиком сами.
    /// </summary>
    private static string BuildQuestionBlock(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        return $"\n\n💬 _Вопрос:_\n{TelegramRenderer.EscapeMarkdownV1(message.Trim())}";
    }

    /// <summary>
    ///     Telegram-блок с контактом студента (#693, зеркало #575). Пустая строка, если Telegram
    ///     не привязан. Валидный @handle → кликабельная t.me-ссылка; иначе — plain-текст (часто
    ///     platform username содержит '-'/'.', невалидные в TG-нике). Raw-значение → handle
    ///     escape'им поэлементно сами.
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
}
