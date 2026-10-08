using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Sessions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Sessions.Queries;

public sealed record GetSessionStatsQuery(Guid SessionId, Guid UserId) : IQuery;

public sealed class GetSessionStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/sessions/{sessionId:guid}/stats",
                async Task<EndpointResult<SessionStatsDto>> (
                    Guid sessionId,
                    GetSessionStatsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetSessionStatsQuery(sessionId, user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Разбивка результатов собственной сессии: общий correct/total + срез по сложности
///     (JUNIOR/MIDDLE/SENIOR) и по теме. correct/graded считаются по авто-грейдимым ответам
///     (с баллом); OPEN_TEXT/неотвеченные входят только в Total. Зеркалит курсовую статистику
///     тестов платформы. Чужая сессия (scoped по UserId) → 404.
/// </summary>
public sealed class GetSessionStatsHandler : IQueryHandlerWithResult<SessionStatsDto, GetSessionStatsQuery>
{
    private readonly ITrainingSessionsRepository _sessions;

    public GetSessionStatsHandler(ITrainingSessionsRepository sessions) => _sessions = sessions;

    public async Task<Result<SessionStatsDto, Error>> Handle(
        GetSessionStatsQuery query,
        CancellationToken cancellationToken)
    {
        Result<TrainingSession, Error> sessionResult = await _sessions.GetWithItemsAsync(
            s => s.Id == query.SessionId && s.UserId == query.UserId,
            cancellationToken);
        if (sessionResult.IsFailure)
            return TrainerServiceErrors.Session.NotFound(query.SessionId);

        TrainingSession session = sessionResult.Value;
        IReadOnlyList<TrainingSessionItem> items = session.Items;

        int answered = items.Count(i => i.AnsweredAt is not null);
        int graded = items.Count(i => i.ScorePercent.HasValue);
        int correct = items.Count(i => i.Verdict == AnswerVerdict.CORRECT);

        var byDifficulty = items
            .GroupBy(i => i.Difficulty ?? "UNSPECIFIED", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(ToBreakdown)
            .ToList();

        var byTopic = items
            .GroupBy(i => i.TopicId)
            .OrderBy(g => g.Key)
            .Select(g => new SessionBreakdownDto(
                g.Key.ToString(),
                g.Count(i => i.Verdict == AnswerVerdict.CORRECT),
                g.Count(i => i.ScorePercent.HasValue),
                g.Count()))
            .ToList();

        return new SessionStatsDto(
            session.Id,
            session.Mode.ToString(),
            session.Status.ToString(),
            session.ScorePercent,
            items.Count,
            answered,
            correct,
            graded,
            byDifficulty,
            byTopic);
    }

    private static SessionBreakdownDto ToBreakdown(IGrouping<string, TrainingSessionItem> group) =>
        new(
            group.Key,
            group.Count(i => i.Verdict == AnswerVerdict.CORRECT),
            group.Count(i => i.ScorePercent.HasValue),
            group.Count());
}
