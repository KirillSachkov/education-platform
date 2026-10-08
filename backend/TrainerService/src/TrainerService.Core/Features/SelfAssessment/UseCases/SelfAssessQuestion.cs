using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.SelfAssessment;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.SelfAssessment;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.SelfAssessment.UseCases;

public sealed record SelfAssessQuestionCommand(
    Guid SessionId,
    Guid ItemId,
    Guid UserId,
    string Verdict) : ICommand;

public sealed class SelfAssessQuestionCommandValidator : AbstractValidator<SelfAssessQuestionCommand>
{
    public SelfAssessQuestionCommandValidator()
    {
        RuleFor(x => x.Verdict)
            .Must(raw => Enum.TryParse<SelfAssessVerdict>(raw, ignoreCase: false, out _))
            .WithError(TrainerServiceErrors.SelfAssess.InvalidVerdict(string.Empty));
    }
}

public sealed class SelfAssessQuestionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/trainer/sessions/{sessionId:guid}/items/{itemId:guid}/self-assess",
                async Task<EndpointResult<SelfAssessmentDto>> (
                    Guid sessionId,
                    Guid itemId,
                    SelfAssessRequest request,
                    SelfAssessQuestionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new SelfAssessQuestionCommand(sessionId, itemId, user.UserId, request.Verdict),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Мягкая самооценка «Не уверен» (#691 t8). Студент жмёт «Не уверен» на вопросе своей сессии →
///     <see cref="QuestionStudyState"/> вопроса апсертится в <see cref="StudyStatus.REVIEW"/> через
///     <see cref="QuestionStudyState.MarkForReview"/>: вопрос планируется на ближний повтор и всплывает
///     в «На повтор» (SRS due) / «Моих ошибках» — БЕЗ пометки WRONG и БЕЗ движения mastery (в отличие
///     от обычной проверки ответа, которая зовёт грейдер + mastery). Own-data: чужая/несуществующая
///     сессия → 404; неизвестный item → 404. Идемпотентно — повторное нажатие держит REVIEW (только
///     пере-планирует повтор). Возвращает новый study-status. <c>UNSURE</c> — пока единственный вердикт
///     (неизвестный → 400). Контракт минимален: «изученность» темы и факт ответа не гейтятся — самооценка
///     доступна на любом не-отвеченном вопросе сессии (UI показывает кнопку только до ответа).
/// </summary>
public sealed class SelfAssessQuestionHandler : ICommandHandler<SelfAssessmentDto, SelfAssessQuestionCommand>
{
    private readonly IValidator<SelfAssessQuestionCommand> _validator;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly ITransactionManager _transactions;

    public SelfAssessQuestionHandler(
        IValidator<SelfAssessQuestionCommand> validator,
        ITrainingSessionsRepository sessions,
        IQuestionStudyStatesRepository studyStates,
        ITransactionManager transactions)
    {
        _validator = validator;
        _sessions = sessions;
        _studyStates = studyStates;
        _transactions = transactions;
    }

    public async Task<Result<SelfAssessmentDto, Error>> Handle(
        SelfAssessQuestionCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Own-data: a foreign (or missing) session is indistinguishable → 404.
        Result<TrainingSession, Error> sessionResult = await _sessions.GetWithItemsAsync(
            s => s.Id == command.SessionId && s.UserId == command.UserId,
            cancellationToken);
        if (sessionResult.IsFailure)
            return TrainerServiceErrors.Session.NotFound(command.SessionId);

        TrainingSession session = sessionResult.Value;

        TrainingSessionItem? item = session.Items.FirstOrDefault(i => i.Id == command.ItemId);
        if (item is null)
            return TrainerServiceErrors.Session.ItemNotFound(command.ItemId);

        // «Не уверен» is a PRE-answer action: self-assessing an already-answered item would force a
        // graded question back into REVIEW (data-integrity). Reject answered items with 409.
        if (item.AnsweredAt is not null)
            return TrainerServiceErrors.SelfAssess.AlreadyAnswered(command.ItemId);

        // Upsert the question's study-state and park it in REVIEW. No mastery write — «Не уверен» is a
        // soft self-assessment, not a graded answer (compare to SessionAnswerGrading which updates mastery).
        Result<QuestionStudyState, Error> existing =
            await _studyStates.GetByAsync(command.UserId, item.QuestionId, cancellationToken);

        QuestionStudyState state;
        if (existing.IsSuccess)
        {
            state = existing.Value;
        }
        else
        {
            state = QuestionStudyState.Create(command.UserId, item.QuestionId, item.TopicId);
            await _studyStates.AddAsync(state, cancellationToken);
        }

        state.MarkForReview(DateTimeOffset.UtcNow);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new SelfAssessmentDto(item.Id, state.Status.ToString());
    }
}
