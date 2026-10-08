using ContentAccess;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Core.Features.Topics;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.Queries;

public sealed record GetTopicsQuery(Guid UserId, bool IsAdmin, Guid? TrackId, string? Direction) : IQuery;

public sealed class GetTopicsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/topics",
                async Task<EndpointResult<IReadOnlyList<TopicListItemDto>>> (
                    GetTopicsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken,
                    Guid? trackId = null,
                    string? direction = null) =>
                    await handler.Handle(
                        new GetTopicsQuery(user.UserId, user.IsAdmin, trackId, direction),
                        cancellationToken))
            // Метаданные тем — не gated (как каталог): аноним просматривает хаб read-only (#614 F).
            // Handler анон-безопасен: UserId == Guid.Empty → mastery пустой, hasPro=false (fail-closed),
            // фильтр IsPublished зашит → DRAFT скрыт. Ответы здесь не отдаются.
            .AllowAnonymousEndpoint();
    }
}

/// <summary>
///     Список PUBLISHED-тем тренажёра, опционально отфильтрованный по треку и направлению,
///     обогащённый персональным mastery вызывающего и фримиум-флагами (есть ли бесплатный
///     банк / заблокирована ли тема целиком). Питает лендинг + прогресс-карту.
///     Метаданные тем — не gated (как каталог курсов).
/// </summary>
public sealed class GetTopicsHandler : IQueryHandlerWithResult<IReadOnlyList<TopicListItemDto>, GetTopicsQuery>
{
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly ITopicMasteryRepository _mastery;
    private readonly ITopicCoverageReader _coverage;
    private readonly IEntitlementChecker _entitlements;

    public GetTopicsHandler(
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        ITopicMasteryRepository mastery,
        ITopicCoverageReader coverage,
        IEntitlementChecker entitlements)
    {
        _topics = topics;
        _banks = banks;
        _questions = questions;
        _mastery = mastery;
        _coverage = coverage;
        _entitlements = entitlements;
    }

    public async Task<Result<IReadOnlyList<TopicListItemDto>, Error>> Handle(
        GetTopicsQuery query,
        CancellationToken cancellationToken)
    {
        Result<TopicDirection?, Error> directionResult = TopicDirectionParser.Parse(query.Direction);
        if (directionResult.IsFailure)
            return directionResult.Error;

        Guid? trackId = query.TrackId is { } id && id != Guid.Empty ? id : null;
        TopicDirection? direction = directionResult.Value;

        IReadOnlyList<Topic> topics = await _topics.GetManyByAsync(
            t => t.IsPublished
                && (trackId == null || t.TrackId == trackId)
                && (direction == null || t.Direction == direction),
            cancellationToken);
        if (topics.Count == 0)
            return new List<TopicListItemDto>();

        var topicIds = topics.Select(t => t.Id).ToHashSet();

        IReadOnlyList<TopicBank> allBanks =
            await _banks.GetManyByAsync(b => topicIds.Contains(b.TopicId), cancellationToken);
        ILookup<Guid, TopicBank> banksByTopic = allBanks.ToLookup(b => b.TopicId);

        // Per-question фримиум (#674): тема даёт бесплатную пробу, если у её STUDY-банков есть ≥1 вопрос
        // с IsFreeSample. Bank-tier (FREE/PAID) для доступа больше НЕ читается (dormant). Тема заперта
        // целиком, только если free-сэмплов нет вовсе (банк лишь из OPEN_TEXT / пуст) и нет PRO.
        var studyBanks = allBanks.Where(b => b.Purpose == BankPurpose.STUDY).ToList();
        var studyBankIds = studyBanks.Select(b => b.Id).ToList();
        Dictionary<Guid, Guid> topicByStudyBank = studyBanks.ToDictionary(b => b.Id, b => b.TopicId);
        IReadOnlyList<TrainerQuestion> freeQuestions = studyBankIds.Count == 0
            ? []
            : await _questions.GetManyByAsync(
                q => studyBankIds.Contains(q.BankId) && q.IsFreeSample, cancellationToken);
        HashSet<Guid> topicsWithFreeSample = freeQuestions
            .Select(q => topicByStudyBank[q.BankId])
            .ToHashSet();

        IReadOnlyList<TopicMastery> masteries = query.UserId == Guid.Empty
            ? []
            : await _mastery.GetManyByAsync(
                m => m.UserId == query.UserId && topicIds.Contains(m.TopicId),
                cancellationToken);
        Dictionary<Guid, TopicMastery> masteryByTopic = masteries.ToDictionary(m => m.TopicId);

        // Освоение (#664) = покрытие: distinct верно отвеченных вопросов / всего вопросов в банках темы.
        // Для анонима (UserId == Guid.Empty) covered = 0 → покрытие 0 у всех тем.
        IReadOnlyDictionary<Guid, TopicCoverage> coverageByTopic =
            await _coverage.GetCoverageAsync(query.UserId, topicIds, cancellationToken);

        // PRO-подписка (cap:TRAINER_PRO) или admin раскрывает PAID-only темы. Один Redis SINTER на запрос.
        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            query.UserId, query.IsAdmin, _entitlements, cancellationToken);

        var items = new List<TopicListItemDto>(topics.Count);
        foreach (Topic topic in topics)
        {
            IEnumerable<TopicBank> banks = banksByTopic[topic.Id];
            bool hasFreeSample = topicsWithFreeSample.Contains(topic.Id);
            bool hasAnyBank = banks.Any();

            // Заперта целиком: банки есть, но ни одного free-сэмпла (только OPEN_TEXT / пусто) и нет PRO.
            bool isLocked = hasAnyBank && !hasFreeSample && !hasPro;
            string? lockReason = isLocked ? TrainerProAccessPolicy.LOCK_REASON_PRO_REQUIRED : null;

            masteryByTopic.TryGetValue(topic.Id, out TopicMastery? mastery);
            coverageByTopic.TryGetValue(topic.Id, out TopicCoverage coverage);

            items.Add(new TopicListItemDto(
                topic.Id,
                topic.TrackId,
                topic.Slug,
                topic.Title,
                topic.Area,
                topic.Description,
                topic.Direction?.ToString(),
                topic.RecommendedCourseId,
                topic.FallbackCourseId,
                mastery?.MasteryPercent ?? 0,
                coverage.Percent,
                mastery?.IsWeak ?? true,
                mastery?.AnswersCount ?? 0,
                hasFreeSample,
                isLocked,
                lockReason));
        }

        return items;
    }
}
