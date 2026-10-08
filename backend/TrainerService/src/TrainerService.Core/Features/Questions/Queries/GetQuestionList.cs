using ContentAccess;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Questions;
using TrainerService.Core.Database;
using TrainerService.Core.Features.Shared;
using TrainerService.Domain;
using TrainerService.Domain.Bookmarks;
using TrainerService.Domain.QuestionStudyStates;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Questions.Queries;

public sealed record GetQuestionListQuery(
    Guid UserId,
    bool IsAdmin,
    Guid TopicId,
    string? Difficulty,
    string? Status,
    string? Type,
    string? Tag) : IQuery;

public sealed class GetQuestionListEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/topics/{topicId:guid}/questions",
                async Task<EndpointResult<QuestionListDto>> (
                    Guid topicId,
                    GetQuestionListHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken,
                    string? difficulty = null,
                    string? status = null,
                    string? type = null,
                    string? tag = null) =>
                    await handler.Handle(
                        new GetQuestionListQuery(user.UserId, user.IsAdmin, topicId, difficulty, status, type, tag),
                        cancellationToken))
            // Метаданные вопросов — не gated (как каталог): для free-тем анонимный просмотр (SEO),
            // PRO-темы помечаются isLocked. Ответы НИКОГДА не отдаются этим эндпоинтом.
            .AllowAnonymousEndpoint();
    }
}

/// <summary>
///     Список вопросов охвата (тема) для режима «Изучение» (#568 Ф2). Резолвит тему (PUBLISHED;
///     admin видит DRAFT) → все её банки → answer-key каждого квиза из ECS → проецирует ТОЛЬКО
///     метаданные вопроса (стем/тип/сложность/секция) <b>без</b> правильных ответов, обогащает
///     персональным статусом (<c>QuestionStudyState</c>; нет строки = NEW) и флагом закладки.
///     Фильтры (difficulty/status/type/tag) применяются в памяти к спроецированному набору.
///     PRO-тема без доступного банка перечисляется с <c>IsLocked=true</c> (метаданные легитимны,
///     ответы не утекают). Анонимам доступен список free-тем (SEO).
/// </summary>
public sealed class GetQuestionListHandler : IQueryHandlerWithResult<QuestionListDto, GetQuestionListQuery>
{
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly IQuestionStudyStatesRepository _studyStates;
    private readonly IBookmarkedQuestionsRepository _bookmarks;
    private readonly IEntitlementChecker _entitlementChecker;

    public GetQuestionListHandler(
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        IQuestionStudyStatesRepository studyStates,
        IBookmarkedQuestionsRepository bookmarks,
        IEntitlementChecker entitlementChecker)
    {
        _topics = topics;
        _banks = banks;
        _questions = questions;
        _studyStates = studyStates;
        _bookmarks = bookmarks;
        _entitlementChecker = entitlementChecker;
    }

    public async Task<Result<QuestionListDto, Error>> Handle(
        GetQuestionListQuery query,
        CancellationToken cancellationToken)
    {
        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == query.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(query.TopicId);

        Topic topic = topicResult.Value;
        if (!topic.IsPublished && !query.IsAdmin)
            return TrainerServiceErrors.Topic.NotPublished(query.TopicId);

        // Только STUDY-банки питают учебный список/охват темы — MOCK-only банки (видимые лишь в
        // симуляции собеса) не должны раздувать список и счётчики «Изучения» (#568).
        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(
                b => b.TopicId == query.TopicId && b.Purpose == BankPurpose.STUDY,
                cancellationToken);
        if (banks.Count == 0)
            return new QuestionListDto(query.TopicId, IsLocked: false, LockReason: null, []);

        bool hasPro = await TrainerProAccessPolicy.HasProAsync(
            query.UserId, query.IsAdmin, _entitlementChecker, cancellationToken);

        // Вопросы из собственного банка тренажёра (#623) — метаданные легитимны даже для locked-вопроса
        // (стем редактится сервером, ответы не проецируются).
        var bankIds = banks.Select(b => b.Id).ToList();
        IReadOnlyList<TrainerQuestion> bankQuestions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), cancellationToken);

        // Per-question фримиум-гейт (#674): доступен ⇔ IsFreeSample || hasPro (admin → hasPro). Bank-tier
        // больше НЕ читается для доступа (dormant). «10%» free-доля = вопросы с IsFreeSample. Тема целиком
        // заперта только если у неё нет ни одного free-сэмпла (только OPEN_TEXT / пусто) и нет PRO.
        bool hasAnyFree = bankQuestions.Any(q => q.IsFreeSample);
        bool topicFullyLocked = !hasAnyFree && !hasPro;

        // Persona-данные: статус изучения + закладки — по реально спроецированным вопросам.
        var questionIds = bankQuestions.Select(q => q.Id).Distinct().ToList();

        Dictionary<Guid, QuestionStudyState> stateByQuestion = query.UserId == Guid.Empty
            ? []
            : (await _studyStates.GetForQuestionsAsync(query.UserId, questionIds, cancellationToken))
                .GroupBy(s => s.QuestionId)
                .ToDictionary(g => g.Key, g => g.First());

        HashSet<Guid> bookmarkedQuestionIds = query.UserId == Guid.Empty
            ? []
            : (await _bookmarks.GetManyByAsync(
                    b => b.UserId == query.UserId && questionIds.Contains(b.QuestionId),
                    cancellationToken))
                .Select(b => b.QuestionId)
                .ToHashSet();

        IReadOnlyList<QuestionListItemDto> items = bankQuestions
            .Select(q =>
            {
                stateByQuestion.TryGetValue(q.Id, out QuestionStudyState? state);
                // Per-question замок (#674): заблокирован ⇔ не free-сэмпл И нет PRO. OPEN_TEXT никогда не
                // free → всегда locked для не-PRO. Admin/PRO → hasPro=true → не locked. Стем редактится
                // сервером (RedactLocked) при locked — метаданные перечисляются, ответы не утекают.
                bool itemLocked = !hasPro && !q.IsFreeSample;
                return LockedContentRedactor.RedactLocked(new QuestionListItemDto(
                    q.Id,
                    q.Stem,
                    q.Type.ToString(),
                    q.Difficulty?.ToString(),
                    q.Section,
                    QuestionStatusText.From(state),
                    bookmarkedQuestionIds.Contains(q.Id),
                    itemLocked,
                    itemLocked ? TrainerProAccessPolicy.LOCK_REASON_PRO_REQUIRED : null));
            })
            .Where(item => MatchesFilters(item, query))
            .ToList();

        return new QuestionListDto(
            query.TopicId,
            topicFullyLocked,
            topicFullyLocked ? TrainerProAccessPolicy.LOCK_REASON_PRO_REQUIRED : null,
            items);
    }

    private static bool MatchesFilters(QuestionListItemDto item, GetQuestionListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Difficulty)
            && !string.Equals(item.Difficulty, query.Difficulty.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Status)
            && !string.Equals(item.Status, query.Status.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Type)
            && !string.Equals(item.Type, query.Type.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Tag-фасет (#568 Ф2): ECS answer-key пока не несёт тегов вопроса, поэтому фильтр по тегу
        // сейчас не матчит ничего (контент-проход с тегами — follow-up, см. отчёт W2). Плумбинг
        // готов: как только теги появятся в проекции — добавить сравнение здесь.
        if (!string.IsNullOrWhiteSpace(query.Tag))
            return false;

        return true;
    }
}
