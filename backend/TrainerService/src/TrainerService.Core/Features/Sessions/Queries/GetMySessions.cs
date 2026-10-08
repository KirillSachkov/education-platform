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

public sealed record GetMySessionsQuery(Guid UserId, string? Mode, Guid? TrackId) : IQuery;

public sealed class GetMySessionsEndpoint : IEndpoint
{
    public const int LIMIT = 50;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/sessions/my",
                async Task<EndpointResult<IReadOnlyList<SessionHistoryItemDto>>> (
                    GetMySessionsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken,
                    string? mode = null,
                    Guid? trackId = null) =>
                    await handler.Handle(new GetMySessionsQuery(user.UserId, mode, trackId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     История сессий вызывающего (newest-first, до 50) — для «вернуться к сессии» + истории.
///     Опционально фильтруется по режиму (DRILL/MOCK) и треку (по сохранённому
///     <see cref="TrainingSession.TrackId"/>). Только собственные данные (scoped по UserId). Без
///     item'ов / ключа грейдинга.
/// </summary>
public sealed class GetMySessionsHandler : IQueryHandlerWithResult<IReadOnlyList<SessionHistoryItemDto>, GetMySessionsQuery>
{
    private readonly ITrainingSessionsRepository _sessions;

    public GetMySessionsHandler(ITrainingSessionsRepository sessions) => _sessions = sessions;

    public async Task<Result<IReadOnlyList<SessionHistoryItemDto>, Error>> Handle(
        GetMySessionsQuery query,
        CancellationToken cancellationToken)
    {
        TrainingMode? modeFilter = null;
        if (!string.IsNullOrWhiteSpace(query.Mode))
        {
            if (!Enum.TryParse(query.Mode.Trim(), ignoreCase: false, out TrainingMode parsed) || !Enum.IsDefined(parsed))
                return TrainerServiceErrors.Session.InvalidMode(query.Mode);

            modeFilter = parsed;
        }

        // Track-фильтр — равенство на сохранённом TrackId сессии (SQL-индексируемое, без array-overlap,
        // который Npgsql не транслирует) (#568). Guid.Empty трактуем как «без фильтра».
        Guid? trackFilter = query.TrackId is { } trackId && trackId != Guid.Empty ? trackId : null;

        IReadOnlyList<TrainingSession> sessions = await _sessions.GetRecentForUserAsync(
            query.UserId, modeFilter, trackFilter, GetMySessionsEndpoint.LIMIT, cancellationToken);

        return sessions
            .Select(s => new SessionHistoryItemDto(
                s.Id,
                s.Mode.ToString(),
                s.Status.ToString(),
                s.TopicIds,
                s.ScorePercent,
                s.Items.Count(i => i.AnsweredAt is not null),
                s.Items.Count,
                s.StartedAt,
                s.CompletedAt,
                s.TimeLimitSeconds,
                s.GradingStatus.ToString()))
            .ToList();
    }
}
