using System.Text.Json;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Stats;
using TrainerService.Core.Database;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetMockTrendQuery(Guid UserId) : IQuery;

public sealed class GetMockTrendEndpoint : IEndpoint
{
    /// <summary>Cap on aggregated weak/strong topic chips so the UI stays readable.</summary>
    public const int TOPIC_CAP = 8;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/stats/mock-trend",
                async Task<EndpointResult<TrainerMockTrendDto>> (
                    GetMockTrendHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMockTrendQuery(user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Тренд мок-собесов вызывающего (#568): баллы завершённых MOCK-сессий (по возрастанию даты) +
///     агрегированные слабые/сильные темы из AI-разбора (<c>AiWeakTopicsJson</c>/<c>AiStrengthsJson</c>),
///     дедуп (case-insensitive) + cap. Темы агрегируются от самых свежих сессий — новые слабости важнее.
///     Нет завершённых моков → пустые массивы (фронт прячет блок). Own-data (scoped по UserId).
/// </summary>
public sealed class GetMockTrendHandler : IQueryHandlerWithResult<TrainerMockTrendDto, GetMockTrendQuery>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITrainingSessionsRepository _sessions;

    public GetMockTrendHandler(ITrainingSessionsRepository sessions) => _sessions = sessions;

    public async Task<Result<TrainerMockTrendDto, Error>> Handle(
        GetMockTrendQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TrainingSession> mocks =
            await _sessions.GetCompletedMockSessionsAsync(query.UserId, cancellationToken);

        if (mocks.Count == 0)
            return new TrainerMockTrendDto([], [], []);

        var attempts = mocks
            .Select(s => new MockAttemptDto(s.Id, s.CompletedAt!.Value, s.ScorePercent ?? 0))
            .ToList();

        // Aggregate topics newest-first so recent weaknesses/strengths win the cap.
        var weak = AggregateTopics(mocks, s => s.AiWeakTopicsJson);
        var strong = AggregateTopics(mocks, s => s.AiStrengthsJson);

        return new TrainerMockTrendDto(attempts, weak, strong);
    }

    private static IReadOnlyList<string> AggregateTopics(
        IReadOnlyList<TrainingSession> mocks,
        Func<TrainingSession, string?> selector)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        // mocks are ascending by date → reverse to take the most recent sessions' topics first.
        foreach (TrainingSession session in mocks.Reverse())
        {
            foreach (string topic in DeserializeStringList(selector(session)))
            {
                string trimmed = topic.Trim();
                if (trimmed.Length == 0 || !seen.Add(trimmed))
                    continue;

                result.Add(trimmed);
                if (result.Count >= GetMockTrendEndpoint.TOPIC_CAP)
                    return result;
            }
        }

        return result;
    }

    private static IReadOnlyList<string> DeserializeStringList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
}
