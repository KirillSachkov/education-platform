using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.Domain.MockInterviews;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.MockInterviews.Queries;

public sealed record GetMockInterviewsQuery(bool IsAdmin) : IQuery;

public sealed class GetMockInterviewsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/mock-interviews",
                async Task<EndpointResult<IReadOnlyList<MockInterviewSummaryDto>>> (
                    GetMockInterviewsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMockInterviewsQuery(user.IsAdmin), cancellationToken))
            // Метаданные симуляций — не gated (как каталог треков/тем): аноним просматривает хаб
            // read-only (#614 F), видит список PUBLISHED; admin видит и DRAFT (IsAdmin=false у анона).
            // Вопросы здесь не отдаются.
            .AllowAnonymousEndpoint();
    }
}

/// <summary>
///     Список симуляций собеседования для хаба. Участник видит PUBLISHED, admin — все.
///     Возвращает только метаданные (slug/title/описание + число тем + реально доступное число
///     вопросов на сессию) — без самих вопросов. #568.
/// </summary>
public sealed class GetMockInterviewsHandler
    : IQueryHandlerWithResult<IReadOnlyList<MockInterviewSummaryDto>, GetMockInterviewsQuery>
{
    /// <summary>Потолок legacy-пула — зеркалит <c>StartMockInterviewSession.MAX_LEGACY_POOL</c>.</summary>
    private const int MAX_LEGACY_POOL = 20;

    private readonly IMockInterviewsRepository _mockInterviews;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;

    public GetMockInterviewsHandler(
        IMockInterviewsRepository mockInterviews,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions)
    {
        _mockInterviews = mockInterviews;
        _banks = banks;
        _questions = questions;
    }

    public async Task<Result<IReadOnlyList<MockInterviewSummaryDto>, Error>> Handle(
        GetMockInterviewsQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MockInterview> interviews = await _mockInterviews.GetManyByAsync(
            m => m.IsPublished || query.IsAdmin,
            cancellationToken);

        // Курированные ссылки — какие реально существуют в банке. Висячие (на удалённые вопросы)
        // пропускаются при старте, поэтому НЕ должны раздувать счётчик на карточке (#623).
        HashSet<Guid> curatedRefs = interviews
            .SelectMany(m => m.Questions.Select(q => q.QuestionId))
            .ToHashSet();
        HashSet<Guid> existingCurated = curatedRefs.Count == 0
            ? []
            : (await _questions.GetManyByAsync(q => curatedRefs.Contains(q.Id), cancellationToken))
                .Select(q => q.Id)
                .ToHashSet();

        // Legacy-симуляции (без курированного набора) — сколько вопросов в банках их тем.
        HashSet<Guid> legacyTopicIds = interviews
            .Where(m => m.Questions.Count == 0)
            .SelectMany(m => m.TopicIds)
            .ToHashSet();
        Dictionary<Guid, int> questionsPerTopic =
            await CountQuestionsPerTopicAsync(legacyTopicIds, cancellationToken);

        return interviews
            .OrderBy(m => m.SortIndex)
            .ThenBy(m => m.CreatedAt)
            .Select(m => new MockInterviewSummaryDto(
                m.Id,
                m.Slug,
                m.Title,
                m.Description,
                m.TopicIds.Count,
                AvailableQuestionCount(m, existingCurated, questionsPerTopic)))
            .ToList();
    }

    /// <summary>Считает доступные вопросы по теме (банки темы → их вопросы) для legacy-симуляций.</summary>
    private async Task<Dictionary<Guid, int>> CountQuestionsPerTopicAsync(
        HashSet<Guid> topicIds, CancellationToken ct)
    {
        var perTopic = new Dictionary<Guid, int>();
        if (topicIds.Count == 0)
            return perTopic;

        IReadOnlyList<TopicBank> banks = await _banks.GetManyByAsync(b => topicIds.Contains(b.TopicId), ct);
        Dictionary<Guid, Guid> topicByBank = banks.ToDictionary(b => b.Id, b => b.TopicId);
        if (topicByBank.Count == 0)
            return perTopic;

        HashSet<Guid> bankIds = topicByBank.Keys.ToHashSet();
        IReadOnlyList<TrainerQuestion> questions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), ct);

        foreach (TrainerQuestion question in questions)
        {
            Guid topicId = topicByBank[question.BankId];
            perTopic[topicId] = perTopic.GetValueOrDefault(topicId) + 1;
        }

        return perTopic;
    }

    /// <summary>
    ///     Реально доступное число вопросов на сессию (то, что увидит студент). Курированный набор:
    ///     <c>min(подвыборка ?? резолвимые, резолвимые)</c> — висячие ссылки не считаются. Legacy по
    ///     темам: вопросы в банках тем, capped до <see cref="MAX_LEGACY_POOL"/> (как при старте).
    ///     <c>0</c> → карточка показывает «вопросы готовятся», старт заблокирован вместо ошибки. #585/#623.
    /// </summary>
    private static int AvailableQuestionCount(
        MockInterview interview,
        HashSet<Guid> existingCurated,
        Dictionary<Guid, int> questionsPerTopic)
    {
        if (interview.Questions.Count > 0)
        {
            int resolvable = interview.Questions.Count(q => existingCurated.Contains(q.QuestionId));
            if (resolvable == 0)
                return 0;

            return interview.QuestionsPerSession is { } perSession
                ? Math.Min(perSession, resolvable)
                : resolvable;
        }

        int legacyAvailable = interview.TopicIds.Sum(topicId => questionsPerTopic.GetValueOrDefault(topicId));
        return Math.Min(legacyAvailable, MAX_LEGACY_POOL);
    }
}
