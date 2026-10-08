using AssignmentReviewService.Contracts.Reviews;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using Core.Abstractions;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AssignmentReviewService.Core.Features.Reviews.UseCases;

public sealed record ReplyToStudentMessageCommand(
    Guid MessageId,
    ReplyToStudentMessageRequest Request) : ICommand;

public sealed class ReplyToStudentMessageValidator
    : AbstractValidator<ReplyToStudentMessageCommand>
{
    // GitHub-кап на тело коммента — 65536 символов. Отсекаем сверх этого ещё до GitHub-вызова.
    public const int MaxReplyBodyLength = 65536;

    public ReplyToStudentMessageValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.Request.Body).NotEmpty();
        RuleFor(x => x.Request.Body).MaximumLength(MaxReplyBodyLength);
    }
}

public sealed class ReplyToStudentMessageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assignment-review/student-messages/{messageId:guid}/reply/",
                async Task<EndpointResult<ReplyToStudentMessageResponse>> (
                    [FromRoute] Guid messageId,
                    [FromBody] ReplyToStudentMessageRequest request,
                    [FromServices] ReplyToStudentMessageHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(
                        new ReplyToStudentMessageCommand(messageId, request), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW)
            .RequireRateLimiting("ar-student-reply");
    }
}

/// <summary>
///     Issue #713 (1b) — автор курса отвечает студенту на его реплику в PR. Ответ постится
///     обратно в ТОТ ЖЕ тред GitHub (reply на inline-коммент или новый коммент PR — по
///     <see cref="StudentPrMessage.Kind"/>) и сохраняется в <c>answer_*</c>-полях.
///
///     Access зеркалит owner-scoped ARS-эндпоинты (<c>SubmitIterationFeedback</c> /
///     <c>RunIteration</c> manual re-check): <c>Progress.VIEW</c> + owner-or-admin, где
///     «владелец» — <b>автор курса</b> (<see cref="AiReview.AuthorId"/>), а не студент.
///     Повторный ответ разрешён — обновляет <c>answer_*</c> и постит ещё один reply.
/// </summary>
public sealed class ReplyToStudentMessageHandler
    : ICommandHandler<ReplyToStudentMessageResponse, ReplyToStudentMessageCommand>
{
    private readonly IStudentPrMessagesRepository _messages;
    private readonly IAiReviewsRepository _reviews;
    private readonly IVcsInstallationsRepository _installations;
    private readonly IVcsProvider _vcs;
    private readonly UserScopedData _user;
    private readonly IValidator<ReplyToStudentMessageCommand> _validator;
    private readonly ILogger<ReplyToStudentMessageHandler> _logger;

    public ReplyToStudentMessageHandler(
        IStudentPrMessagesRepository messages,
        IAiReviewsRepository reviews,
        IVcsInstallationsRepository installations,
        IVcsProvider vcs,
        UserScopedData user,
        IValidator<ReplyToStudentMessageCommand> validator,
        ILogger<ReplyToStudentMessageHandler> logger)
    {
        _messages = messages;
        _reviews = reviews;
        _installations = installations;
        _vcs = vcs;
        _user = user;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<ReplyToStudentMessageResponse, Error>> Handle(
        ReplyToStudentMessageCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation =
            await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("review.reply.invalid", validation.Errors[0].ErrorMessage);

        // Пустой/whitespace body → 400. `.NotEmpty()` пропускает "   ", поэтому whitespace
        // ловим отдельно; заодно тримим, чтобы в GitHub и в БД лёг один и тот же текст.
        string body = command.Request.Body?.Trim() ?? string.Empty;
        if (body.Length == 0)
            return Error.Validation("review.reply.empty", "Текст ответа не может быть пустым.");

        StudentPrMessage? message = await _messages.GetByIdAsync(command.MessageId, ct);
        if (message is null)
            return Error.NotFound("review.student_message.not_found",
                $"Сообщение студента {command.MessageId} не найдено.");

        AiReview? review = await _reviews.GetByAsync(r => r.Id == message.AiReviewId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(message.AiReviewId);

        // Tier-2 ownership: отвечать может только автор курса (или админ), не студент.
        if (!_user.IsOwnerOrAdmin(review.AuthorId))
            return ReviewErrors.AccessDenied();

        // Installation для repo owner'а — как в RunIteration (owner = первый сегмент full_name).
        string ownerLogin = review.RepoFullName.Split('/', 2)[0].ToLowerInvariant();
        VcsInstallation? installation = await _installations.GetByAsync(
            i => i.Provider == VcsProvider.GITHUB
                 && i.OwnerLogin == ownerLogin
                 && i.Status == VcsInstallationStatus.ACTIVE,
            ct);
        if (installation is null)
            return ReviewErrors.NoInstallation(review.RepoFullName);

        Result<VcsPostedComment, Error> postResult = await _vcs.PostCommentReplyAsync(
            installation.InstallationId,
            review.RepoFullName,
            review.PullNumber,
            message.Kind,
            inReplyToCommentId: message.GitHubCommentId,
            body,
            ct);
        if (postResult.IsFailure)
            return MapVcsError(postResult.Error, review.RepoFullName);

        DateTimeOffset answeredAt = DateTimeOffset.UtcNow;
        await _messages.MarkAnsweredAsync(
            message.Id, body, postResult.Value.GitHubCommentId, answeredAt, ct);

        _logger.LogInformation(
            "Author {AuthorId} replied to student message {MessageId} (kind={Kind}) on review {ReviewId} " +
            "→ posted GitHub comment {CommentId}.",
            _user.UserId, message.Id, message.Kind, review.Id, postResult.Value.GitHubCommentId);

        return new ReplyToStudentMessageResponse(
            message.Id,
            postResult.Value.GitHubCommentId,
            postResult.Value.HtmlUrl,
            answeredAt);
    }

    private static Error MapVcsError(Error vcsError, string repoFullName)
    {
        string code = vcsError.Messages[0].Code;
        return code switch
        {
            "vcs.pull_request.not_found" => ReviewErrors.GitHubUnavailable(
                $"тред PR {repoFullName} не найден"),
            "vcs.invalid_request" => ReviewErrors.GitHubInvalidRequest(vcsError.Messages[0].Message),
            _ => ReviewErrors.GitHubUnavailable(vcsError.Messages[0].Message),
        };
    }
}
