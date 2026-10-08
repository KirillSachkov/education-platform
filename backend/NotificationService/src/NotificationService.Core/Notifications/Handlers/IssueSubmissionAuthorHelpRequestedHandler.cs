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
/// <c>progress.events / issue_submission.author_help_requested</c> → автору курса (#383).
///
/// Студент нажал «Позвать автора» — AI-ассистент не заменяет живую проверку, и автор
/// подключается по явному запросу студента. Параллельно резолвит issue title из ECS и
/// student display name из AuthService — оба вызова cached, на failure любого — graceful
/// fallback. Линк ведёт на author review-страницу (через <c>PlatformLinkBuilder</c>).
/// </summary>
public sealed class IssueSubmissionAuthorHelpRequestedHandler
{
    private readonly INotificationDispatcher _dispatcher;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IAuthServiceClient _authClient;
    private readonly ILogger<IssueSubmissionAuthorHelpRequestedHandler> _logger;

    public IssueSubmissionAuthorHelpRequestedHandler(
        INotificationDispatcher dispatcher,
        IEducationContentServiceClient ecsClient,
        IAuthServiceClient authClient,
        ILogger<IssueSubmissionAuthorHelpRequestedHandler> logger)
    {
        _dispatcher = dispatcher;
        _ecsClient = ecsClient;
        _authClient = authClient;
        _logger = logger;
    }

    public async Task Handle(IssueSubmissionAuthorHelpRequested evt, CancellationToken ct)
    {
        // Параллельный fetch — оба вызова cached, но на miss идут в HTTP.
        Task<string> issueTitleTask = ResolveIssueTitleAsync(evt.IssueId, ct);
        Task<AuthUserLookupDto?> studentTask = ResolveStudentAsync(evt.StudentUserId, ct);
        await Task.WhenAll(issueTitleTask, studentTask);
        string issueTitle = await issueTitleTask;
        AuthUserLookupDto? student = await studentTask;

        string studentName = student?.Name ?? student?.Username ?? "студент";

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.AuthorHelpRequested,
            recipientUserId: evt.AuthorId,
            correlationId: evt.SubmissionId,
            // #575 — t.me-ссылка студента + текст просьбы. Raw-блоки: значения собраны здесь
            // с поэлементным MarkdownV1-escape (handle / сообщение — user-derived).
            args: TemplateArgs.Of(
                    ("issueTitle", issueTitle),
                    ("studentName", studentName))
                .WithRaw("studentContactTg", BuildTelegramContactBlock(student?.TelegramUsername))
                .WithRaw("helpMessageTg", BuildHelpMessageBlock(evt.Message)),
            payload: new { submissionId = evt.SubmissionId, issueId = evt.IssueId, courseId = evt.CourseId });

        await _dispatcher.DispatchAsync(request, ct);
    }

    private async Task<string> ResolveIssueTitleAsync(Guid issueId, CancellationToken ct)
    {
        Result<IssueSearchLookupDto, Error> lookup = await _ecsClient.GetIssueSearchLookupAsync(issueId, ct);
        if (lookup.IsSuccess && lookup.Value is not null)
            return lookup.Value.Title;

        _logger.LogWarning(
            "ECS lookup failed for issue {IssueId}: {Error}. Using fallback.",
            issueId, lookup.ErrorText());
        return "задача";
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
    ///     Telegram-блок с контактом студента (#575). Пустая строка, если Telegram не привязан.
    ///     <c>provider_display_name</c> = реальный @handle при наличии, иначе fallback на platform
    ///     username (часто содержит '-'/'.', невалидные в TG-нике) → деградируем до plain-текста
    ///     вместо битой t.me-ссылки. Raw-значение → handle escape'им поэлементно сами.
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
    ///     Telegram-блок с текстом «в чём нужна помощь» (#575). Пустая строка, если студент
    ///     не указал текст. Raw-значение → сообщение escape'им целиком сами.
    /// </summary>
    private static string BuildHelpMessageBlock(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        return $"\n\n💬 _Просьба:_\n{TelegramRenderer.EscapeMarkdownV1(message.Trim())}";
    }
}
