using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.MockInterviews;
using TrainerService.Domain.Questions;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.MockInterviews.Queries;

public sealed record GetMockInterviewBuilderQuery(Guid MockInterviewId) : IQuery;

public sealed class GetMockInterviewBuilderEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/mock-interviews/{mockInterviewId:guid}/builder",
                async Task<EndpointResult<MockInterviewBuilderDto>> (
                    Guid mockInterviewId,
                    GetMockInterviewBuilderHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMockInterviewBuilderQuery(mockInterviewId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Детальная карточка симуляции для редактора автора (admin): метаданные + размер подвыборки +
///     курированный набор вопросов с метаданными (стем/тип/сложность) из локального банка тренажёра и
///     темой-источником (банк вопроса → тема). Порядок сохраняется по SortIndex; висячие ссылки
///     (удалённый вопрос) пропускаются. #585/#623.
/// </summary>
public sealed class GetMockInterviewBuilderHandler
    : IQueryHandlerWithResult<MockInterviewBuilderDto, GetMockInterviewBuilderQuery>
{
    private readonly IMockInterviewsRepository _mockInterviews;
    private readonly ITopicBanksRepository _banks;
    private readonly ITopicsRepository _topics;
    private readonly ITrainerQuestionsRepository _questions;

    public GetMockInterviewBuilderHandler(
        IMockInterviewsRepository mockInterviews,
        ITopicBanksRepository banks,
        ITopicsRepository topics,
        ITrainerQuestionsRepository questions)
    {
        _mockInterviews = mockInterviews;
        _banks = banks;
        _topics = topics;
        _questions = questions;
    }

    public async Task<Result<MockInterviewBuilderDto, Error>> Handle(
        GetMockInterviewBuilderQuery query,
        CancellationToken cancellationToken)
    {
        Result<MockInterview, Error> interviewResult =
            await _mockInterviews.GetByAsync(m => m.Id == query.MockInterviewId, cancellationToken);
        if (interviewResult.IsFailure)
            return TrainerServiceErrors.MockInterview.NotFound(query.MockInterviewId);

        MockInterview interview = interviewResult.Value;

        // Резолвим курированные вопросы из локального банка тренажёра (#623).
        var curatedQuestionIds = interview.Questions.Select(q => q.QuestionId).ToHashSet();
        IReadOnlyList<TrainerQuestion> resolvedQuestions = curatedQuestionIds.Count == 0
            ? []
            : await _questions.GetManyByAsync(q => curatedQuestionIds.Contains(q.Id), cancellationToken);
        Dictionary<Guid, TrainerQuestion> questionById = resolvedQuestions.ToDictionary(q => q.Id);

        // Тема-источник вопроса = тема его банка.
        var bankIds = resolvedQuestions.Select(q => q.BankId).ToHashSet();
        IReadOnlyList<TopicBank> banks = bankIds.Count == 0
            ? []
            : await _banks.GetManyByAsync(b => bankIds.Contains(b.Id), cancellationToken);
        Dictionary<Guid, Guid> topicByBank = banks.ToDictionary(b => b.Id, b => b.TopicId);

        var topicIds = topicByBank.Values.ToHashSet();
        IReadOnlyList<Topic> topics = topicIds.Count == 0
            ? []
            : await _topics.GetManyByAsync(t => topicIds.Contains(t.Id), cancellationToken);
        Dictionary<Guid, string> topicTitleById = topics.ToDictionary(t => t.Id, t => t.Title);

        var questions = new List<MockInterviewBuilderQuestionDto>();
        foreach (MockInterviewQuestion reference in interview.Questions.OrderBy(q => q.SortIndex))
        {
            if (!questionById.TryGetValue(reference.QuestionId, out TrainerQuestion? question))
                continue; // вопрос удалён из банка — пропускаем висячую ссылку.

            Guid? topicId = topicByBank.TryGetValue(question.BankId, out Guid tid) ? tid : null;
            string? topicTitle = topicId is { } t && topicTitleById.TryGetValue(t, out string? title) ? title : null;

            questions.Add(new MockInterviewBuilderQuestionDto(
                reference.QuestionId,
                question.Stem,
                question.Type.ToString(),
                question.Difficulty?.ToString(),
                topicId,
                topicTitle));
        }

        return new MockInterviewBuilderDto(
            interview.Id,
            interview.Slug,
            interview.Title,
            interview.Description,
            interview.IsPublished,
            interview.QuestionsPerSession,
            questions);
    }
}
