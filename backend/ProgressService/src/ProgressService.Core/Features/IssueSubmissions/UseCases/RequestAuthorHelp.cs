using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Database;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using SharedKernel;

namespace ProgressService.Core.Features.IssueSubmissions.UseCases;

public sealed record RequestAuthorHelpCommand(Guid SubmissionId, string? Message) : ICommand;

public sealed class RequestAuthorHelpValidator : AbstractValidator<RequestAuthorHelpCommand>
{
    public RequestAuthorHelpValidator()
    {
        RuleFor(x => x.SubmissionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(RequestAuthorHelpCommand.SubmissionId)));

        RuleFor(x => x.Message)
            .MaximumLength(IssueSubmission.AUTHOR_HELP_MESSAGE_MAX_LENGTH)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(RequestAuthorHelpCommand.Message)));
    }
}

public sealed class RequestAuthorHelpEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/submissions/{submissionId:guid}/request-author-help/",
                async Task<EndpointResult> (
                    [FromRoute] Guid submissionId,
                    [FromBody] RequestAuthorHelpRequest? request,
                    [FromServices] RequestAuthorHelpHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(
                        new RequestAuthorHelpCommand(submissionId, request?.Message), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

/// <summary>
///     #383 «Позвать автора»: студент явно подключает автора курса к проверке своего
///     решения (AI-ассистент не справился / нужна живая помощь). По умолчанию автор вне
///     цикла AI-проверки — это первичная точка его входа.
///
///     Ownership: вызывающий обязан быть владельцем submission (через
///     IssueSubmission → IssueProgress → CourseEnrollment.UserId == текущий пользователь).
///     Idempotent — повторный вызов возвращает 200, но событие публикуется ровно один раз
///     (на первом реальном переходе).
/// </summary>
public sealed class RequestAuthorHelpHandler : ICommandHandler<RequestAuthorHelpCommand>
{
    private readonly IIssueSubmissionRepository _submissions;
    private readonly IIssueProgressRepository _progresses;
    private readonly ICourseEnrollmentRepository _enrollments;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly IValidator<RequestAuthorHelpCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<RequestAuthorHelpHandler> _logger;

    public RequestAuthorHelpHandler(
        IIssueSubmissionRepository submissions,
        IIssueProgressRepository progresses,
        ICourseEnrollmentRepository enrollments,
        IOutboxService outbox,
        ITransactionManager transactions,
        IValidator<RequestAuthorHelpCommand> validator,
        UserScopedData user,
        ILogger<RequestAuthorHelpHandler> logger)
    {
        _submissions = submissions;
        _progresses = progresses;
        _enrollments = enrollments;
        _outbox = outbox;
        _transactions = transactions;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(RequestAuthorHelpCommand command, CancellationToken ct)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return validation.ToError();

        Result<IssueSubmission, Error> submissionResult = await _submissions.GetByAsync(
            s => s.Id == command.SubmissionId, ct);
        if (submissionResult.IsFailure)
            return submissionResult.Error;

        IssueSubmission submission = submissionResult.Value;

        Result<IssueProgress, Error> progressResult = await _progresses.GetByAsync(
            p => p.Id == submission.IssueProgressId, ct);
        if (progressResult.IsFailure)
            return progressResult.Error;

        IssueProgress progress = progressResult.Value;

        Result<CourseEnrollment, Error> enrollmentResult = await _enrollments.GetByAsync(
            e => e.Id == progress.EnrollmentId, ct);
        if (enrollmentResult.IsFailure)
            return enrollmentResult.Error;

        CourseEnrollment enrollment = enrollmentResult.Value;

        if (!_user.IsAdmin && enrollment.UserId != _user.UserId)
            return Error.Authorization("submission.request_author_help.not_authorized",
                "Только владелец решения может позвать автора.");

        DateTime now = DateTime.UtcNow;
        UnitResult<Error> request = submission.RequestAuthorHelp(now, command.Message, out bool firstRequest);
        if (request.IsFailure)
            return request.Error;

        // Событие публикуем ровно один раз — на первом переходе. Повторный «зов»
        // не плодит уведомления автору. AuthorId/CourseId берём из enrollment'а,
        // IssueId — из issue_progress.
        if (firstRequest)
        {
            await _outbox.PublishAsync(new IssueSubmissionAuthorHelpRequested(
                SubmissionId: submission.Id,
                IssueProgressId: progress.Id,
                StudentUserId: enrollment.UserId,
                AuthorId: enrollment.AuthorId,
                IssueId: progress.IssueId,
                CourseId: enrollment.CourseId,
                RequestedAt: new DateTimeOffset(DateTime.SpecifyKind(now, DateTimeKind.Utc)),
                Message: submission.AuthorHelpMessage));
        }

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation(
            "Author help requested for submission {SubmissionId} by user {UserId} (firstRequest={FirstRequest}).",
            command.SubmissionId,
            _user.UserId,
            firstRequest);
        return UnitResult.Success<Error>();
    }
}
