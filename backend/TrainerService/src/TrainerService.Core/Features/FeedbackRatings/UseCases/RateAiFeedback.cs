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
using TrainerService.Contracts.FeedbackRatings;
using TrainerService.Core.Database;
using TrainerService.Core.Grading;
using TrainerService.Domain;
using TrainerService.Domain.FeedbackRatings;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.FeedbackRatings.UseCases;

public sealed record RateAiFeedbackCommand(
    Guid SessionId,
    Guid ItemId,
    Guid UserId,
    string Rating) : ICommand;

public sealed class RateAiFeedbackCommandValidator : AbstractValidator<RateAiFeedbackCommand>
{
    public RateAiFeedbackCommandValidator()
    {
        RuleFor(x => x.Rating)
            .Must(raw => Enum.TryParse<FeedbackRating>(raw, ignoreCase: false, out _))
            .WithError(TrainerServiceErrors.FeedbackRating.InvalidRating(string.Empty));
    }
}

public sealed class RateAiFeedbackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/sessions/{sessionId:guid}/answers/{itemId:guid}/feedback-rating",
                async Task<EndpointResult<AiFeedbackRatingDto>> (
                    Guid sessionId,
                    Guid itemId,
                    RateAiFeedbackRequest request,
                    RateAiFeedbackHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new RateAiFeedbackCommand(sessionId, itemId, user.UserId, request.Rating),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Оценка студентом AI-разбора («Разбор ИИ») одного отвеченного открытого вопроса своей сессии
///     (#691 t7). Own-data: сессия резолвится по <c>(Id, UserId)</c> — чужая → 404. Оценить можно
///     только готовый AI-разбор (OPEN_TEXT с финальным вердиктом — не PENDING); иначе 409. Апсерт
///     идемпотентен: та же оценка повторно → no-op 200, противоположная → переключение (UP↔DOWN).
///     Возвращает текущую оценку.
/// </summary>
public sealed class RateAiFeedbackHandler : ICommandHandler<AiFeedbackRatingDto, RateAiFeedbackCommand>
{
    private readonly IValidator<RateAiFeedbackCommand> _validator;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly IAiFeedbackRatingsRepository _ratings;
    private readonly ITransactionManager _transactions;

    public RateAiFeedbackHandler(
        IValidator<RateAiFeedbackCommand> validator,
        ITrainingSessionsRepository sessions,
        IAiFeedbackRatingsRepository ratings,
        ITransactionManager transactions)
    {
        _validator = validator;
        _sessions = sessions;
        _ratings = ratings;
        _transactions = transactions;
    }

    public async Task<Result<AiFeedbackRatingDto, Error>> Handle(
        RateAiFeedbackCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        FeedbackRating rating = Enum.Parse<FeedbackRating>(command.Rating, ignoreCase: false);

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

        // Only a finished AI разбор is rateable: an answered OPEN_TEXT with a final verdict (not PENDING).
        // Closed / unanswered / still-grading items have no «Разбор ИИ» to thumb.
        bool isOpenText = string.Equals(item.QuestionType, AnswerGrader.OPEN_TEXT, StringComparison.Ordinal);
        bool isGraded = item.AnsweredAt is not null
            && item.Verdict is not null
            && item.Verdict != AnswerVerdict.PENDING;
        if (!isOpenText || !isGraded)
            return TrainerServiceErrors.FeedbackRating.NotRateable();

        Result<AiFeedbackRating, Error> existing = await _ratings.GetByAsync(
            r => r.UserId == command.UserId && r.SessionItemId == command.ItemId,
            cancellationToken);

        if (existing.IsSuccess)
        {
            // Same rating again → idempotent no-op (no write). Opposite → toggle + persist.
            if (existing.Value.Rating != rating)
            {
                existing.Value.ChangeRating(rating);
                UnitResult<Error> updateResult = await _transactions.SaveChangesAsync(cancellationToken);
                if (updateResult.IsFailure)
                    return updateResult.Error;
            }

            return new AiFeedbackRatingDto(command.ItemId, existing.Value.Rating.ToString());
        }

        AiFeedbackRating created = AiFeedbackRating.Create(
            command.UserId, session.Id, item.Id, item.QuestionId, rating);
        await _ratings.AddAsync(created, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            // Concurrent first-rating race: between the "not found" read above and this insert, a parallel
            // POST for the same (UserId, SessionItemId) won → the unique index (user_id, session_item_id)
            // rejected ours (Postgres 23505). The DbUpdateException is surfaced by ITransactionManager as a
            // failure Result (its SaveChanges catches it), not rethrown — so we recover off the failure
            // result: re-read the row that landed and return it idempotently (same shape as "already rated").
            // No row → it's a genuine (non-race) error, so propagate the original failure.
            Result<AiFeedbackRating, Error> raced = await _ratings.GetByAsync(
                r => r.UserId == command.UserId && r.SessionItemId == command.ItemId,
                cancellationToken);
            if (raced.IsSuccess)
                return new AiFeedbackRatingDto(command.ItemId, raced.Value.Rating.ToString());

            return saveResult.Error;
        }

        return new AiFeedbackRatingDto(command.ItemId, created.Rating.ToString());
    }
}
