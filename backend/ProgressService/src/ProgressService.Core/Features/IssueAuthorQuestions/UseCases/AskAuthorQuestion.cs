using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Ownership;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Database;
using ProgressService.Domain;
using ProgressService.Domain.AuthorQuestions;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace ProgressService.Core.Features.IssueAuthorQuestions.UseCases;

public sealed record AskAuthorQuestionCommand(Guid IssueId, string? Message) : ICommand;

public sealed class AskAuthorQuestionValidator : AbstractValidator<AskAuthorQuestionCommand>
{
    public AskAuthorQuestionValidator()
    {
        RuleFor(x => x.IssueId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(AskAuthorQuestionCommand.IssueId)));

        RuleFor(x => x.Message)
            .NotEmpty()
            .WithError(ProgressErrors.IssueAuthorQuestionMessageRequired())
            .MaximumLength(IssueAuthorQuestion.MESSAGE_MAX_LENGTH)
            .WithError(ProgressErrors.IssueAuthorQuestionMessageTooLong(IssueAuthorQuestion.MESSAGE_MAX_LENGTH));
    }
}

public sealed class AskAuthorQuestionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/progress/issues/{issueId:guid}/ask-author/",
                async Task<EndpointResult> (
                    [FromRoute] Guid issueId,
                    [FromBody] AskAuthorQuestionRequest? request,
                    [FromServices] AskAuthorQuestionHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(
                        new AskAuthorQuestionCommand(issueId, request?.Message), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW);
    }
}

/// <summary>
///     #693 «Задать вопрос автору» — приватный issue-scoped аналог submission-scoped «Позвать
///     автора» (#383). Студент, зависший на чтении непонятного задания ДО отправки решения,
///     явно зовёт автора. Идемпотентно: одна запись на пару (UserId, IssueId), событие
///     <see cref="IssueAuthorQuestionAsked"/> публикуется ровно один раз (на первом вопросе).
///
///     Tier-1: <c>Progress.VIEW</c>. Tier-3: per-issue entitlement (как в SubmitIssue) — нет
///     доступа к заданию → 403. Автор/курс резолвятся через ECS (graceful — недоступность ECS
///     не валит запрос в 500).
/// </summary>
public sealed class AskAuthorQuestionHandler : ICommandHandler<AskAuthorQuestionCommand>
{
    private readonly IIssueAuthorQuestionRepository _questions;
    private readonly IEducationContentServiceClient _ecsClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly IValidator<AskAuthorQuestionCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<AskAuthorQuestionHandler> _logger;

    public AskAuthorQuestionHandler(
        IIssueAuthorQuestionRepository questions,
        IEducationContentServiceClient ecsClient,
        IEntitlementChecker entitlementChecker,
        IOutboxService outbox,
        ITransactionManager transactions,
        IValidator<AskAuthorQuestionCommand> validator,
        UserScopedData user,
        ILogger<AskAuthorQuestionHandler> logger)
    {
        _questions = questions;
        _ecsClient = ecsClient;
        _entitlementChecker = entitlementChecker;
        _outbox = outbox;
        _transactions = transactions;
        _validator = validator;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(AskAuthorQuestionCommand command, CancellationToken ct)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return validation.ToError();

        // Tier-3: конкретное задание может быть gated по плану. Тот же per-item check, что у
        // SubmitIssue — нет доступа к заданию → нельзя задать вопрос (subject = текущий юзер,
        // admin bypass — внутри checker'а).
        AccessSubject subject = _user.ToAccessSubject();
        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            subject, ResourceTypes.ISSUE, command.IssueId, ct);
        if (!decision.IsGranted)
            return ProgressErrors.IssueAuthorQuestionAccessDenied();

        // Идемпотентность: запись уже есть → 200, событие повторно не публикуется.
        bool alreadyAsked = await _questions.ExistsAsync(
            q => q.UserId == _user.UserId && q.IssueId == command.IssueId, ct);
        if (alreadyAsked)
        {
            _logger.LogInformation(
                "Issue author question already exists for issue {IssueId} by user {UserId} — no-op.",
                command.IssueId, _user.UserId);
            return UnitResult.Success<Error>();
        }

        (Guid authorId, Guid? courseId) = await ResolveOwnershipAsync(command.IssueId, ct);

        Result<IssueAuthorQuestion, Error> created = IssueAuthorQuestion.Create(
            _user.UserId, command.IssueId, command.Message, DateTime.UtcNow);
        if (created.IsFailure)
            return created.Error;

        IssueAuthorQuestion question = created.Value;
        await _questions.AddAsync(question, ct);

        await _outbox.PublishAsync(new IssueAuthorQuestionAsked(
            QuestionId: question.Id,
            StudentUserId: question.UserId,
            AuthorId: authorId,
            IssueId: question.IssueId,
            CourseId: courseId,
            Message: question.Message,
            AskedAt: new DateTimeOffset(DateTime.SpecifyKind(question.AskedAt, DateTimeKind.Utc))));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation(
            "Issue author question asked for issue {IssueId} by user {UserId} (author {AuthorId}, course {CourseId}).",
            command.IssueId, _user.UserId, authorId, courseId);
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Резолвит автора задания (через entity-ownership) и его основной курс (первый binding).
    ///     ECS недоступен / нет данных → graceful: пустой автор / null курс, запрос не валится.
    /// </summary>
    private async Task<(Guid AuthorId, Guid? CourseId)> ResolveOwnershipAsync(Guid issueId, CancellationToken ct)
    {
        Guid authorId = Guid.Empty;
        Result<EntityOwnershipDto, Error> ownership = await _ecsClient.GetEntityOwnershipAsync(
            ResourceTypes.ISSUE, issueId, ct);
        if (ownership.IsSuccess && ownership.Value.AuthorId is Guid resolvedAuthor)
        {
            authorId = resolvedAuthor;
        }
        else
        {
            _logger.LogWarning(
                "ECS ownership lookup failed for issue {IssueId}: {Error}. Author left empty.",
                issueId, ownership.IsFailure ? ownership.Error.GetMessage() : "no author");
        }

        Guid? courseId = null;
        Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error> bindings =
            await _ecsClient.GetIssueCourseBindingsAsync([issueId], ct);
        if (bindings.IsSuccess)
        {
            courseId = bindings.Value.FirstOrDefault(b => b.IssueId == issueId)?.CourseId;
        }
        else
        {
            _logger.LogWarning(
                "ECS course-binding lookup failed for issue {IssueId}: {Error}. Course left null.",
                issueId, bindings.Error.GetMessage());
        }

        return (authorId, courseId);
    }
}
