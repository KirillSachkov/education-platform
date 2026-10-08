using AssignmentReviewService.Core.AiSettings;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Errors;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.Reviews;
using Core.Abstractions;
using Core.Database;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AssignmentReviewService.Core.Features.Reviews.UseCases;

/// <summary>
///     Студенческая доработка после AI-approve с замечаниями (#725). Когда AI принял
///     задание с вердиктом <see cref="AiReviewVerdict.MINOR_ISSUES"/>, submission уже
///     <c>APPROVED</c>/<c>COMPLETED</c> и зачёт с XP выданы — но студент может захотеть
///     закрыть указанные замечания. Этот endpoint даёт ему опциональную петлю: запушил
///     фиксы в тот же PR → «Проверить снова» → новая итерация на существующем
///     <see cref="AiReview"/>.
///
///     <para>
///     Петля многоразовая (#976). Гейт «последний вердикт == MINOR_ISSUES» делал её
///     одноразовой: первый же re-run, вернувший MAJOR/OFF_TOPIC (или упавший —
///     <c>OnIterationFailed</c> обнуляет <c>LatestVerdict</c>), закрывал доработку
///     навсегда, а ре-сабмит уже невозможен (submission APPROVED, а
///     <c>IssueProgress.SubmitForReview</c> пускает только из IN_PROGRESS /
///     REQUESTED_CHANGES) — студент оставался с замечаниями и без способа их закрыть.
///     </para>
///
///     <para>
///     Кредит НЕ отбирается: приходящий <c>AiReviewIterationCompleted</c> по уже-APPROVED
///     submission в ProgressService проходит через <c>ApplyVerdictGate</c>, который
///     срабатывает только из <c>PENDING</c> → статус/XP не трогаются, обновляется лишь
///     denorm-вердикт. Тот же <see cref="RunAiReviewRequested"/> раннер, что и у авторского
///     <c>run-iteration</c>: within-review incremental diff + same-SHA replay без LLM'а
///     (если студент не запушил новых коммитов).
///     </para>
/// </summary>
public sealed record RequestStudentRerunCommand(Guid AiReviewId) : ICommand;

public sealed class RequestStudentRerunValidator : AbstractValidator<RequestStudentRerunCommand>
{
    public RequestStudentRerunValidator()
    {
        RuleFor(x => x.AiReviewId).NotEmpty();
    }
}

public sealed class RequestStudentRerunEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assignment-review/reviews/{id:guid}/student-rerun/",
                async Task<EndpointResult<RequestRunIterationResponse>> (
                    [FromRoute] Guid id,
                    [FromServices] RequestStudentRerunHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new RequestStudentRerunCommand(id), ct))
            .RequirePermissions(PlatformPermissions.Progress.VIEW)
            .RequireRateLimiting("ar-review-iteration");
    }
}

/// <summary>
///     Fast-fail gate + publish <see cref="RunAiReviewRequested"/> в durable outbox.
///     Гейт: владелец submission (<c>review.UserId</c>) + по сдаче уже есть хотя бы одна
///     завершённая AI-итерация + не <c>RUNNING</c>. Тяжёлую работу
///     делает <see cref="RunAiReviewRequestedHandler"/> в отдельном Wolverine-scope'е —
///     как и авторский <see cref="RequestRunIterationHandler"/>.
/// </summary>
public sealed class RequestStudentRerunHandler
    : ICommandHandler<RequestRunIterationResponse, RequestStudentRerunCommand>
{
    private readonly IAiReviewsRepository _reviews;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly IValidator<RequestStudentRerunCommand> _validator;
    private readonly IAssignmentReviewAiModelSettingsResolver _settingsResolver;
    private readonly ILogger<RequestStudentRerunHandler> _logger;

    public RequestStudentRerunHandler(
        IAiReviewsRepository reviews,
        IOutboxService outbox,
        ITransactionManager transactions,
        UserScopedData user,
        IValidator<RequestStudentRerunCommand> validator,
        IAssignmentReviewAiModelSettingsResolver settingsResolver,
        ILogger<RequestStudentRerunHandler> logger)
    {
        _reviews = reviews;
        _outbox = outbox;
        _transactions = transactions;
        _user = user;
        _validator = validator;
        _settingsResolver = settingsResolver;
        _logger = logger;
    }

    public async Task<Result<RequestRunIterationResponse, Error>> Handle(
        RequestStudentRerunCommand command, CancellationToken ct)
    {
        FluentValidation.Results.ValidationResult validation = await _validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
            return Error.Validation("review.rerun.invalid", validation.Errors[0].ErrorMessage);

        // Платформенный тумблер AI-проверки (#355) — выключен, значит доработка недоступна.
        if (!await _settingsResolver.ResolveReviewEnabledAsync(ct))
            return ReviewErrors.ReviewDisabled();

        AiReview? review = await _reviews.GetByAsync(r => r.Id == command.AiReviewId, ct);
        if (review is null)
            return ReviewErrors.ReviewNotFound(command.AiReviewId);

        // Владелец = студент-сабмиттер (review.UserId), не автор курса. Зеркалит
        // GetReviewBySubmission — тот же owner-or-admin гейт для студенческого доступа.
        // Authorization (403), а не Failure (500) как ARS-AccessDenied: «не владелец» —
        // не сбой сервера. Зеркалит student-facing FinalizeSubmission.
        if (!_user.IsOwnerOrAdmin(review.UserId))
            return Error.Authorization("review.rerun.access_denied",
                "Повторную проверку может запустить только владелец решения.");

        // Ядро #725 в редакции #976: доработка доступна, пока по сдаче уже прошла хотя бы
        // одна завершённая итерация — то есть AI успела вынести вердикт и студенту есть что
        // закрывать. Смотреть на ПОСЛЕДНИЙ вердикт нельзя: он схлопывает петлю после первого
        // же MAJOR или упавшей итерации (см. XML-комментарий команды). Лишний прогон
        // безопасен — зачёт неотбираем (`ApplyVerdictGate` в ProgressService срабатывает
        // только из PENDING), а стоимость держат rate-limit'ы (RateLimitChecker + 5/min).
        if (!review.Iterations.Any(i => i.Status == AiReviewIterationStatus.COMPLETED))
            return ReviewErrors.RerunNotAvailable();

        // In-memory guard против двойного клика; DB-level running-lock внутри handler'а —
        // настоящий exclusivity-gate.
        if (review.Status == AiReviewStatus.RUNNING)
            return ReviewErrors.ReviewAlreadyRunning(review.Id);

        // Студент не может ни override'ить модель, ни снимать hard-cap на размер diff'а —
        // оба false (в отличие от авторского run-iteration, который снимает cap).
        await _outbox.PublishAsync(new RunAiReviewRequested(review.Id, ModelOverride: null));

        UnitResult<Error> save = await _transactions.SaveChangesAsync(ct);
        if (save.IsFailure)
            return save.Error;

        _logger.LogInformation(
            "Queued student re-run for AI review {AiReviewId} (submission {SubmissionId}).",
            review.Id, review.SubmissionId);

        return new RequestRunIterationResponse(review.Id, Status: "QUEUED");
    }
}
