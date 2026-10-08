using System.Text.Json;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Stats;
using TrainerService.Core.Database;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.Topics;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetStrengthsQuery(Guid UserId) : IQuery;

public sealed class GetStrengthsEndpoint : IEndpoint
{
    /// <summary>Cap on strong/weak topics per column so the UI stays readable.</summary>
    public const int TOPIC_CAP = 8;

    /// <summary>An AI mock-topic counts as a de-noised hint only if it appears in this many distinct mocks.</summary>
    public const int MOCK_HINT_MIN_OCCURRENCES = 2;

    /// <summary>Cap on the secondary AI mock-hint chips.</summary>
    public const int MOCK_HINT_CAP = 6;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/stats/strengths",
                async Task<EndpointResult<TrainerStrengthsDto>> (
                    GetStrengthsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetStrengthsQuery(user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Сильные и слабые стороны вызывающего по ИЗМЕРЕННОМУ per-topic mastery (#614 H) поверх всей
///     активности тренажёра (drill/learn/test/mock), не из одного AI-разбора. Тема попадает в колонки
///     только при &gt;= <see cref="TopicMastery.MIN_ATTEMPTS_FOR_SIGNAL"/> оценённых ответах
///     (отсекает шум малой выборки и неотвеченные темы). Сильные — mastery &gt;= 75 (desc) И есть хотя бы
///     один отвеченный MIDDLE/SENIOR-вопрос (#691 — не коронуем тему за Junior-only); слабые —
///     &lt; 60 (asc), cap по 8. Плюс контекст объёма (sample size) и де-шумленный вторичный AI-сигнал
///     из мок-собесов (тема — только если упомянута в &gt;= 2 завершённых моках). Own-data (scoped по UserId).
/// </summary>
public sealed class GetStrengthsHandler : IQueryHandlerWithResult<TrainerStrengthsDto, GetStrengthsQuery>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITopicMasteryRepository _mastery;
    private readonly ITopicsRepository _topics;
    private readonly ITrainingSessionsRepository _sessions;

    public GetStrengthsHandler(
        ITopicMasteryRepository mastery,
        ITopicsRepository topics,
        ITrainingSessionsRepository sessions)
    {
        _mastery = mastery;
        _topics = topics;
        _sessions = sessions;
    }

    public async Task<Result<TrainerStrengthsDto, Error>> Handle(
        GetStrengthsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<TopicMastery> masteries =
            await _mastery.GetManyByAsync(m => m.UserId == query.UserId, cancellationToken);

        // Only topics with enough graded answers count — one-off attempts are too noisy, and unattempted
        // topics (no mastery row at all) never appear, which is what stops them flooding «подтянуть».
        var assessed = masteries
            .Where(m => m.AnswersCount >= TopicMastery.MIN_ATTEMPTS_FOR_SIGNAL)
            .ToList();

        int sessionsCount = await _sessions.CountSessionsForUserAsync(query.UserId, cancellationToken);
        IReadOnlyList<TrainingSession> completedMocks =
            await _sessions.GetCompletedMockSessionsAsync(query.UserId, cancellationToken);

        var sampleSize = new StrengthSampleSizeDto(
            AssessedTopics: assessed.Count,
            TotalAnswers: assessed.Sum(m => m.AnswersCount),
            SessionsCount: sessionsCount,
            MockCount: completedMocks.Count);

        IReadOnlyList<string> mockHints = DeNoisedMockHintTopics(completedMocks);

        if (assessed.Count == 0)
            return new TrainerStrengthsDto([], [], sampleSize, mockHints);

        // Resolve topic titles for the assessed topics (single batched read, no entity-graph load).
        var topicIds = assessed.Select(m => m.TopicId).ToList();
        IReadOnlyList<Topic> topics =
            await _topics.GetManyByAsync(t => topicIds.Contains(t.Id), cancellationToken);
        Dictionary<Guid, string> titleByTopic = topics.ToDictionary(t => t.Id, t => t.Title);

        // Strong-gate (#691): a topic is «strong» only if it ALSO has >=1 answered MIDDLE or SENIOR
        // question — high mastery off Junior-only answers must not crown a topic. Weak / min-attempts
        // logic is unchanged.
        IReadOnlySet<Guid> midSeniorTopics =
            await _sessions.GetTopicIdsWithMidOrSeniorAnswersAsync(query.UserId, cancellationToken);

        var strong = assessed
            .Where(m => m.MasteryPercent >= TopicMastery.STRONG_THRESHOLD && midSeniorTopics.Contains(m.TopicId))
            .OrderByDescending(m => m.MasteryPercent)
            .ThenByDescending(m => m.AnswersCount)
            .Take(GetStrengthsEndpoint.TOPIC_CAP)
            .Select(m => ToDto(m, titleByTopic))
            .ToList();

        var weak = assessed
            .Where(m => m.MasteryPercent < TopicMastery.WEAK_THRESHOLD)
            .OrderBy(m => m.MasteryPercent)
            .ThenByDescending(m => m.AnswersCount)
            .Take(GetStrengthsEndpoint.TOPIC_CAP)
            .Select(m => ToDto(m, titleByTopic))
            .ToList();

        return new TrainerStrengthsDto(strong, weak, sampleSize, mockHints);
    }

    private static StrengthTopicDto ToDto(TopicMastery m, IReadOnlyDictionary<Guid, string> titleByTopic) =>
        new(m.TopicId,
            titleByTopic.GetValueOrDefault(m.TopicId, "Тема"),
            m.MasteryPercent,
            m.AnswersCount);

    /// <summary>
    ///     De-noised mock-AI supplement: a topic from the AI weak/strong feedback counts only if it shows up
    ///     in at least <see cref="GetStrengthsEndpoint.MOCK_HINT_MIN_OCCURRENCES"/> DISTINCT completed mocks —
    ///     this drops one-off LLM mentions. Counted per session (each topic once per session), newest-first,
    ///     deduped (case-insensitive), capped.
    /// </summary>
    private static IReadOnlyList<string> DeNoisedMockHintTopics(IReadOnlyList<TrainingSession> mocks)
    {
        if (mocks.Count < GetStrengthsEndpoint.MOCK_HINT_MIN_OCCURRENCES)
            return [];

        // Count distinct sessions each topic was mentioned in (across both weak + strong AI lists).
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (TrainingSession session in mocks)
        {
            var perSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string topic in DeserializeStringList(session.AiWeakTopicsJson)
                         .Concat(DeserializeStringList(session.AiStrengthsJson)))
            {
                string trimmed = topic.Trim();
                if (trimmed.Length > 0)
                    perSession.Add(trimmed);
            }

            foreach (string topic in perSession)
                occurrences[topic] = occurrences.GetValueOrDefault(topic) + 1;
        }

        return occurrences
            .Where(kv => kv.Value >= GetStrengthsEndpoint.MOCK_HINT_MIN_OCCURRENCES)
            .OrderByDescending(kv => kv.Value)
            .Select(kv => kv.Key)
            .Take(GetStrengthsEndpoint.MOCK_HINT_CAP)
            .ToList();
    }

    private static IReadOnlyList<string> DeserializeStringList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
}
