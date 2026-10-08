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
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using SharedKernel;

namespace ProgressService.Core.Features.IssueSubmissions.UseCases;

public sealed record FinalizeSubmissionCommand(Guid SubmissionId) : ICommand;

public sealed class FinalizeSubmissionValidator : AbstractValidator<FinalizeSubmissionCommand>
{
    public FinalizeSubmissionValidator()
    {
        RuleFor(x => x.SubmissionId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(FinalizeSubmissionCommand.SubmissionId)));
    }
}

public sealed class FinalizeSubmissionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/submissions/{submissionId:guid}/finalize/",
                async Task<EndpointResult> (
                    [FromRoute] Guid submissionId,
                    [FromServices] FinalizeSubmissionHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new FinalizeSubmissionCommand(submissionId), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

/// <summary>
///     Phase 8 (#15): студент явно отдаёт submission на ручное ревью автору.
///     Защищён ownership-чеком (UserId должен совпадать с владельцем enrollment'а).
///     Idempotent — повторный вызов на уже finalize'нутую submission возвращает success.
/// </summary>
public sealed class FinalizeSubmissionHandler : ICommandHandler<FinalizeSubmissionCommand>
{
    private readonly IIssueSubmissionRepository _submissions;
    private readonly IIssueProgressRepository _progresses;
    private readonly ICourseEnrollmentRepository _enrollments;
    private readonly ITransactionManager _transactions;
    private readonly IValidator<FinalizeSubmissionCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<FinalizeSubmissionHandler> _logger;

    public FinalizeSubmissionHandler(
        IIssueSubmissionRepository submissions,
        IIssueProgressRepository progresses,
        ICourseEnrollmentRepository enrollments,
        ITransactionManager transactions,
        IValidator<FinalizeSubmissionCommand> validator,
        UserScopedData user,
        ILogger<FinalizeSubmissionHandler> logger)
    {
        _submissions = submissions;
        _progresses = progresses;
        _enrollments = enrollments;
        _transactions = transactions;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(FinalizeSubmissionCommand command, CancellationToken ct)
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

        Result<CourseEnrollment, Error> enrollmentResult = await _enrollments.GetByAsync(
            e => e.Id == progressResult.Value.EnrollmentId, ct);
        if (enrollmentResult.IsFailure)
            return enrollmentResult.Error;

        if (!_user.IsAdmin && enrollmentResult.Value.UserId != _user.UserId)
            return Error.Authorization("submission.finalize.not_authorized",
                "Только владелец submission может её финализировать.");

        UnitResult<Error> finalize = submission.Finalize();
        if (finalize.IsFailure)
            return finalize.Error;

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation(
            "Submission {SubmissionId} finalized by user {UserId}.",
            command.SubmissionId,
            _user.UserId);
        return UnitResult.Success<Error>();
    }
}
