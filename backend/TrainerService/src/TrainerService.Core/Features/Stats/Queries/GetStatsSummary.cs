using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Stats;
using TrainerService.Core.Database;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetStatsSummaryQuery(Guid UserId) : IQuery;

public sealed class GetStatsSummaryEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/stats/summary",
                async Task<EndpointResult<TrainerStatsSummaryDto>> (
                    GetStatsSummaryHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetStatsSummaryQuery(user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     All-time сводка тренажёра вызывающего (#568): объём отвеченного + точность (по авто-грейдимым
///     item'ам), разбивка study-статусов («покрытие материала»), число изученных вопросов, точность по
///     сложности (J/M/S всегда, источник — снапшот <c>TrainingSessionItem.Difficulty</c>) и SRS-прогноз.
///     Агрегации считаются в SQL; в память тянутся только маленькие группировки. Own-data (scoped по UserId).
/// </summary>
public sealed class GetStatsSummaryHandler : IQueryHandlerWithResult<TrainerStatsSummaryDto, GetStatsSummaryQuery>
{
    // Difficulty buckets are always returned in this order, even with zero data.
    private static readonly string[] DifficultyOrder = ["JUNIOR", "MIDDLE", "SENIOR"];

    private readonly ITrainingSessionsRepository _sessions;
    private readonly IQuestionStudyStatesRepository _studyStates;

    public GetStatsSummaryHandler(
        ITrainingSessionsRepository sessions,
        IQuestionStudyStatesRepository studyStates)
    {
        _sessions = sessions;
        _studyStates = studyStates;
    }

    public async Task<Result<TrainerStatsSummaryDto, Error>> Handle(
        GetStatsSummaryQuery query,
        CancellationToken cancellationToken)
    {
        SessionAnswerAggregate answers =
            await _sessions.GetAnswerAggregateForUserAsync(query.UserId, cancellationToken);
        StudyStateAggregate study =
            await _studyStates.GetStudyStateAggregateForUserAsync(query.UserId, cancellationToken);

        int allTimeAccuracy = answers.TotalAnswered == 0
            ? 0
            : (int)Math.Round(answers.TotalCorrect * 100.0 / answers.TotalAnswered, MidpointRounding.AwayFromZero);

        var difficultyByKey = answers.ByDifficulty
            .Where(d => d.Difficulty is not null)
            .ToDictionary(d => d.Difficulty!, StringComparer.Ordinal);

        var difficultyAccuracy = DifficultyOrder
            .Select(level =>
            {
                difficultyByKey.TryGetValue(level, out DifficultyAnswerCount? row);
                int answered = row?.Answered ?? 0;
                int correct = row?.Correct ?? 0;
                int accuracy = answered == 0
                    ? 0
                    : (int)Math.Round(correct * 100.0 / answered, MidpointRounding.AwayFromZero);
                return new DifficultyAccuracyDto(level, answered, correct, accuracy);
            })
            .ToList();

        var statusBreakdown = study.StatusCounts
            .Select(c => new StudyStatusCountDto(c.Status.ToString(), c.Count))
            .OrderBy(c => c.Status, StringComparer.Ordinal)
            .ToList();

        int retention = study.TotalTimesSeen == 0
            ? 0
            : (int)Math.Round(study.TotalTimesKnown * 100.0 / study.TotalTimesSeen, MidpointRounding.AwayFromZero);

        var upcoming = study.Upcoming
            .Select(u => new SrsUpcomingDayDto(u.Date, u.Due))
            .ToList();

        var srs = new SrsForecastDto(study.DueToday, upcoming, retention);

        return new TrainerStatsSummaryDto(
            answers.TotalAnswered,
            allTimeAccuracy,
            statusBreakdown,
            study.StudiedQuestions,
            difficultyAccuracy,
            srs);
    }
}
