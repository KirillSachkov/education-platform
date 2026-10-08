using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PlatformAuth.Authorization;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;
using TrainerService.Domain.Tracks;

namespace TrainerService.Core.Features.MockInterviews.Queries;

public sealed record GetQuestionBankQuery(Guid? TrackId, Guid? TopicId) : IQuery;

public sealed class GetQuestionBankEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/question-bank",
                async Task<EndpointResult<IReadOnlyList<QuestionBankItemDto>>> (
                    GetQuestionBankHandler handler,
                    CancellationToken cancellationToken,
                    Guid? trackId = null,
                    Guid? topicId = null) =>
                    await handler.Handle(new GetQuestionBankQuery(trackId, topicId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Источник пикера курированного набора (admin): перечисляет доступные для выбора вопросы. Резолвит
///     PUBLISHED-темы (опц. фильтр по trackId/topicId) → их банки (любого назначения/тира — это
///     curation-источник) → вопросы локального банка тренажёра → плоский список вопросов с банком/темой/
///     треком-источником (#623). Потолок — <see cref="MAX_ITEMS"/> (лог если усечён).
/// </summary>
public sealed class GetQuestionBankHandler
    : IQueryHandlerWithResult<IReadOnlyList<QuestionBankItemDto>, GetQuestionBankQuery>
{
    /// <summary>Потолок числа вопросов в пикере — чтобы не выдать тысячи строк одним запросом.</summary>
    public const int MAX_ITEMS = 500;

    private readonly ITopicsRepository _topics;
    private readonly ITracksRepository _tracks;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;
    private readonly ILogger<GetQuestionBankHandler> _logger;

    public GetQuestionBankHandler(
        ITopicsRepository topics,
        ITracksRepository tracks,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions,
        ILogger<GetQuestionBankHandler> logger)
    {
        _topics = topics;
        _tracks = tracks;
        _banks = banks;
        _questions = questions;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<QuestionBankItemDto>, Error>> Handle(
        GetQuestionBankQuery query,
        CancellationToken cancellationToken)
    {
        Guid? trackId = query.TrackId is { } tr && tr != Guid.Empty ? tr : null;
        Guid? topicId = query.TopicId is { } tp && tp != Guid.Empty ? tp : null;

        IReadOnlyList<Topic> topics = await _topics.GetManyByAsync(
            t => t.IsPublished
                && (trackId == null || t.TrackId == trackId)
                && (topicId == null || t.Id == topicId),
            cancellationToken);
        if (topics.Count == 0)
            return new List<QuestionBankItemDto>();

        var topicIds = topics.Select(t => t.Id).ToHashSet();
        Dictionary<Guid, Topic> topicById = topics.ToDictionary(t => t.Id);

        var trackIds = topics.Select(t => t.TrackId).ToHashSet();
        IReadOnlyList<Track> tracks = await _tracks.GetManyByAsync(t => trackIds.Contains(t.Id), cancellationToken);
        Dictionary<Guid, string> trackTitleById = tracks.ToDictionary(t => t.Id, t => t.Title);

        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(b => topicIds.Contains(b.TopicId), cancellationToken);
        if (banks.Count == 0)
            return new List<QuestionBankItemDto>();

        Dictionary<Guid, Guid> topicByBank = banks.ToDictionary(b => b.Id, b => b.TopicId);
        var bankIds = banks.Select(b => b.Id).ToList();

        IReadOnlyList<TrainerQuestion> questions =
            await _questions.GetManyByAsync(q => bankIds.Contains(q.BankId), cancellationToken);

        var items = new List<QuestionBankItemDto>();
        bool truncated = false;

        foreach (TrainerQuestion question in questions)
        {
            if (items.Count >= MAX_ITEMS)
            {
                truncated = true;
                break;
            }

            Guid bankTopicId = topicByBank[question.BankId];
            Topic topic = topicById[bankTopicId];
            string trackTitle = trackTitleById.GetValueOrDefault(topic.TrackId, string.Empty);

            items.Add(new QuestionBankItemDto(
                question.Id,
                question.Stem,
                question.Type.ToString(),
                question.Difficulty?.ToString(),
                question.BankId,
                topic.Id,
                topic.Title,
                topic.TrackId,
                trackTitle));
        }

        if (truncated)
        {
            _logger.LogInformation(
                "Question-bank picker truncated to {Max} items (trackId={TrackId}, topicId={TopicId}).",
                MAX_ITEMS, trackId, topicId);
        }

        return items;
    }
}
