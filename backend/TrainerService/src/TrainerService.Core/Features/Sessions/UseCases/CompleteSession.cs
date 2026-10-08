using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.UseCases;

public sealed record CompleteSessionCommand(Guid SessionId, Guid UserId) : ICommand;

public sealed class CompleteSessionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/sessions/{sessionId:guid}/complete",
                async Task<EndpointResult<SessionSummaryDto>> (
                    Guid sessionId,
                    CompleteSessionHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new CompleteSessionCommand(sessionId, user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Финализирует сессию: неотвеченный автогрейдимый вопрос считается за 0, а уже AI-оценённый
///     OPEN_TEXT участвует своим баллом. Неоценённый OPEN_TEXT исключается до фонового MOCK-грейда.
/// </summary>
public sealed class CompleteSessionHandler : ICommandHandler<SessionSummaryDto, CompleteSessionCommand>
{
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IMockGradingQueue _gradingQueue;
    private readonly ITransactionManager _transactions;

    public CompleteSessionHandler(
        ITrainingSessionsRepository sessions,
        IMockGradingQueue gradingQueue,
        ITransactionManager transactions)
    {
        _sessions = sessions;
        _gradingQueue = gradingQueue;
        _transactions = transactions;
    }

    public async Task<Result<SessionSummaryDto, Error>> Handle(
        CompleteSessionCommand command,
        CancellationToken cancellationToken)
    {
        Result<TrainingSession, Error> sessionResult = await _sessions.GetWithItemsAsync(
            s => s.Id == command.SessionId && s.UserId == command.UserId,
            cancellationToken);
        if (sessionResult.IsFailure)
            return TrainerServiceErrors.Session.NotFound(command.SessionId);

        TrainingSession session = sessionResult.Value;

        IReadOnlyList<TrainingSessionItem> items = session.Items;

        int scorePercent = SessionScoreCalculator.ComputeAtCompletion(items);

        UnitResult<Error> completeResult = session.Complete(scorePercent);
        if (completeResult.IsFailure)
            return completeResult.Error;

        // Open answers (OPEN_TEXT) get AI-graded out-of-band after Complete (#585). Provisional
        // ScorePercent above includes deterministic scores plus any open answer already graded inline;
        // the background grader recomputes it once pending MOCK answers are scored. Mark PENDING + enqueue only if there is an open answer to
        // grade — pure auto-graded sessions stay NOT_REQUIRED. Enqueue AFTER save so the grader
        // never reads a not-yet-persisted PENDING row.
        bool requiresDeferredGrading = session.Mode == TrainingMode.MOCK && items.Any(i =>
            string.Equals(i.QuestionType, AnswerGrader.OPEN_TEXT, StringComparison.Ordinal)
            && i.AnsweredAt is not null
            && i.ScorePercent is null);
        if (requiresDeferredGrading)
            session.MarkGradingPending();

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        if (requiresDeferredGrading)
            _gradingQueue.Enqueue(session.Id);

        int answered = items.Count(i => i.AnsweredAt is not null);
        int correct = items.Count(i => i.Verdict == AnswerVerdict.CORRECT);

        return new SessionSummaryDto(
            session.Id,
            session.Status.ToString(),
            session.ScorePercent ?? scorePercent,
            items.Count,
            answered,
            correct,
            session.CompletedAt,
            session.GradingStatus.ToString());
    }
}
