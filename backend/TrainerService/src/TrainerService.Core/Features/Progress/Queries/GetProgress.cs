using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Progress;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Progress.Queries;

public sealed record GetProgressQuery(Guid UserId) : IQuery;

public sealed class GetProgressEndpoint : IEndpoint
{
    public const int RECENT_SESSIONS_LIMIT = 20;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/progress",
                async Task<EndpointResult<TrainerProgressDto>> (
                    GetProgressHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetProgressQuery(user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Прогресс вызывающего: per-topic mastery + study-аналитика («изучено / ошибок» из
///     <c>QuestionStudyState</c>, #568 Ф2) + недавние сессии (newest-first). Темы объединяются из
///     <c>TopicMastery</c> И <c>QuestionStudyState</c> — карточки пишут study-state без mastery,
///     поэтому одних mastery-строк недостаточно.
/// </summary>
public sealed class GetProgressHandler : IQueryHandlerWithResult<TrainerProgressDto, GetProgressQuery>
{
    private readonly ITopicMasteryRepository _mastery;
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly ITrainingSessionsRepository _sessions;
    private readonly ITopicCoverageReader _coverage;

    public GetProgressHandler(
        ITopicMasteryRepository mastery,
        IQuestionStudyStatesRepository studyStates,
        ITrainingSessionsRepository sessions,
        ITopicCoverageReader coverage)
    {
        _mastery = mastery;
        _studyStates = studyStates;
        _sessions = sessions;
        _coverage = coverage;
    }

    public async Task<Result<TrainerProgressDto, Error>> Handle(
        GetProgressQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TopicMastery> masteries =
            await _mastery.GetManyByAsync(m => m.UserId == query.UserId, cancellationToken);
        Dictionary<Guid, TopicMastery> masteryByTopic = masteries.ToDictionary(m => m.TopicId);

        IReadOnlyList<QuestionStudyState> studyStates =
            await _studyStates.GetAllForUserAsync(query.UserId, cancellationToken);
        ILookup<Guid, QuestionStudyState> studyByTopic = studyStates.ToLookup(s => s.TopicId);

        // Union тем из mastery и study-state: карточки пишут study-state без mastery-строки.
        var topicIds = masteryByTopic.Keys
            .Union(studyStates.Select(s => s.TopicId))
            .ToList();

        // Освоение (#664) = покрытие: distinct верно отвеченных вопросов / всего вопросов в банках темы.
        IReadOnlyDictionary<Guid, TopicCoverage> coverageByTopic =
            await _coverage.GetCoverageAsync(query.UserId, topicIds, cancellationToken);

        var masteryDtos = topicIds
            .Select(topicId =>
            {
                masteryByTopic.TryGetValue(topicId, out TopicMastery? m);
                coverageByTopic.TryGetValue(topicId, out TopicCoverage coverage);
                List<QuestionStudyState> states = studyByTopic[topicId].ToList();

                int studiedCount = states.Count; // любая строка = «изучено» (видел/знает/повтор/ошибся).
                int mistakesCount = states.Count(s =>
                    s.Status is StudyStatus.WRONG or StudyStatus.REVIEW);

                DateTime lastPractisedAt = LatestActivity(m, states);

                return new TopicMasteryDto(
                    topicId,
                    m?.MasteryPercent ?? 0,
                    coverage.Percent,
                    m?.IsWeak ?? true,
                    m?.AnswersCount ?? 0,
                    studiedCount,
                    mistakesCount,
                    lastPractisedAt);
            })
            .OrderByDescending(dto => dto.LastPractisedAt)
            .ToList();

        IReadOnlyList<TrainingSession> sessions =
            await _sessions.GetRecentSummariesForUserAsync(
                query.UserId,
                GetProgressEndpoint.RECENT_SESSIONS_LIMIT,
                cancellationToken);

        var recent = sessions
            .Select(s => new RecentSessionDto(
                s.Id,
                s.Mode.ToString(),
                s.Status.ToString(),
                s.TopicIds,
                s.ScorePercent,
                s.StartedAt,
                s.CompletedAt))
            .ToList();

        return new TrainerProgressDto(masteryDtos, recent);
    }

    /// <summary>Самая свежая активность темы: max(mastery.LastPractisedAt, study-state.LastSeenAt).</summary>
    private static DateTime LatestActivity(TopicMastery? mastery, IReadOnlyList<QuestionStudyState> states)
    {
        DateTime latest = mastery?.LastPractisedAt ?? DateTime.MinValue;
        foreach (QuestionStudyState s in states)
        {
            DateTime seen = s.LastSeenAt.UtcDateTime;
            if (seen > latest)
                latest = seen;
        }

        return latest;
    }
}
