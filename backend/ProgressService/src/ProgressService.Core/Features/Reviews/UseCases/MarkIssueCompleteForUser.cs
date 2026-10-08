using Core.Abstractions;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Issues;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace ProgressService.Core.Features.Reviews.UseCases;

/// <summary>
///     Ручная приёмка задачи студенту, который НИКОГДА не сдавал работу (#398). В отличие от
///     <see cref="MarkIssueCompleteHandler"/> (force-approve СУЩЕСТВУЮЩЕГО submission'а), здесь
///     submission'а ещё нет — его надо создать синтетически. Решение владельца: построить полную
///     progress-цепочку «как если бы студент дошёл сам» (enrollment-anchor → ProjectProgress →
///     ModuleProgress → IssueProgress), создать синтетический принятый <see cref="IssueSubmission"/>
///     и прогнать обычный каскад через <see cref="IssueSubmission.ForceApprove"/> — единообразно с
///     <see cref="MarkIssueCompleteHandler"/>. Каскад (IssueProgress.Approve → COMPLETED +
///     XP/project/module + integration event <c>issue_submission.approved</c>) отрабатывает
///     существующими guard'ами без изменений.
///     <para>
///     Идемпотентность: если <see cref="IssueProgress"/> уже COMPLETED (задачу приняли раньше) —
///     no-op 200, XP не дублируется.
///     </para>
/// </summary>
public sealed record MarkIssueCompleteForUserCommand(
    Guid CourseId,
    Guid IssueId,
    MarkIssueCompleteForUserRequest Request) : ICommand;

public sealed class MarkIssueCompleteForUserCommandValidator : AbstractValidator<MarkIssueCompleteForUserCommand>
{
    public MarkIssueCompleteForUserCommandValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkIssueCompleteForUserCommand.CourseId)));
        RuleFor(x => x.IssueId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkIssueCompleteForUserCommand.IssueId)));
        RuleFor(x => x.Request)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkIssueCompleteForUserCommand.Request)));
        RuleFor(x => x.Request.UserId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(MarkIssueCompleteForUserRequest.UserId)));
    }
}

public sealed class MarkIssueCompleteForUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/courses/{courseId:guid}/issues/{issueId:guid}/mark-complete-for-user",
            async Task<EndpointResult> (
                    Guid courseId,
                    Guid issueId,
                    MarkIssueCompleteForUserRequest request,
                    MarkIssueCompleteForUserHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new MarkIssueCompleteForUserCommand(courseId, issueId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Progress.MANAGE);
    }
}

public sealed class MarkIssueCompleteForUserHandler : ICommandHandler<MarkIssueCompleteForUserCommand>
{
    private readonly IStaffIssueCompletionService _staffIssueCompletion;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IValidator<MarkIssueCompleteForUserCommand> _validator;
    private readonly UserScopedData _user;

    public MarkIssueCompleteForUserHandler(
        IStaffIssueCompletionService staffIssueCompletion,
        IEducationContentServiceClient ecsClient,
        IValidator<MarkIssueCompleteForUserCommand> validator,
        UserScopedData user)
    {
        _staffIssueCompletion = staffIssueCompletion;
        _ecsClient = ecsClient;
        _validator = validator;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(MarkIssueCompleteForUserCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        IssueReviewFeedback? feedback = null;
        if (command.Request.Feedback is not null)
        {
            Result<IssueReviewFeedback, Error> feedbackResult = IssueReviewFeedback.Create(command.Request.Feedback);
            if (feedbackResult.IsFailure)
            {
                return feedbackResult.Error;
            }

            feedback = feedbackResult.Value;
        }

        // Course-lookup нужен для auth (AuthorId резолвится из ECS, т.к. enrollment студента может
        // ещё не существовать) и переиспользуется shared-сервисом (AuthorId для enrollment-anchor'а).
        Result<CourseDto, Error> courseLookupResult = await _ecsClient
            .GetCourseLookupAsync(command.CourseId, cancellationToken);
        if (courseLookupResult.IsFailure)
        {
            return courseLookupResult.Error;
        }

        // Auth: ADMIN | MODERATOR — привилегированные; AUTHOR — только владелец курса. Зеркалит
        // проверку в ApproveIssue / MarkIssueComplete.
        bool isPrivileged = _user.HasRole(PlatformRoles.ADMIN) || _user.HasRole(PlatformRoles.MODERATOR);
        bool isAuthorOwner = _user.HasRole(PlatformRoles.AUTHOR)
            && courseLookupResult.Value.AuthorId == _user.UserId;
        if (!isPrivileged && !isAuthorOwner)
        {
            return Error.Authorization("review.not.authorized", "Нет прав на рецензирование в этом курсе");
        }

        // Эффективный ревьюер. Человек (автор/админ/модератор из браузера) имеет реальный UserId из
        // JWT → пишем его. Service-токен (client_credentials, mcp-admin) имеет sub=client_id →
        // UserId=Guid.Empty; тогда привилегированный caller передаёт явный ReviewerId-override
        // (зеркало MaterialProcessingService RequestedBy), который и записывается принявшим. Без
        // override остаётся Guid.Empty → fail-closed ниже (#505).
        Guid reviewerId = _user.UserId != Guid.Empty
            ? _user.UserId
            : (isPrivileged && command.Request.ReviewerId is { } overrideReviewerId && overrideReviewerId != Guid.Empty
                ? overrideReviewerId
                : Guid.Empty);

        // Fail-closed ДО любых side-effect'ов: двухфазный flush в сервисе закоммитит progress-цепочку
        // до ForceApprove, поэтому пустой reviewerId оставил бы phantom-progress. Отбиваемся здесь.
        if (reviewerId == Guid.Empty)
        {
            return Error.Validation(
                "progress.review.reviewer.required",
                "Не удалось определить ревьюера: service-токен без userId должен передать ReviewerId");
        }

        return await _staffIssueCompletion.CompleteIssueForUserAsync(
            command.Request.UserId,
            command.CourseId,
            command.IssueId,
            reviewerId,
            feedback,
            courseLookupResult.Value,
            cancellationToken);
    }
}
