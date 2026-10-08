using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.Queries;

public sealed record GetTopicBanksQuery(Guid TopicId) : IQuery;

public sealed class GetTopicBanksEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/topics/{topicId:guid}/banks",
                async Task<EndpointResult<IReadOnlyList<TopicBankAdminDto>>> (
                    Guid topicId,
                    GetTopicBanksHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetTopicBanksQuery(topicId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Admin-список банков вопросов темы (по SortKey) — для редактора темы; с числом вопросов (#623).</summary>
public sealed class GetTopicBanksHandler
    : IQueryHandlerWithResult<IReadOnlyList<TopicBankAdminDto>, GetTopicBanksQuery>
{
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITrainerQuestionsRepository _questions;

    public GetTopicBanksHandler(
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITrainerQuestionsRepository questions)
    {
        _topics = topics;
        _banks = banks;
        _questions = questions;
    }

    public async Task<Result<IReadOnlyList<TopicBankAdminDto>, Error>> Handle(
        GetTopicBanksQuery query,
        CancellationToken cancellationToken)
    {
        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == query.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(query.TopicId);

        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(b => b.TopicId == query.TopicId, cancellationToken);
        if (banks.Count == 0)
            return new List<TopicBankAdminDto>();

        // Число вопросов на банк — батч-fetch вопросов всех банков темы, группировка в памяти.
        var bankIds = banks.Select(b => b.Id).ToList();
        Dictionary<Guid, int> countByBank = (await _questions.GetManyByAsync(
                q => bankIds.Contains(q.BankId), cancellationToken))
            .GroupBy(q => q.BankId)
            .ToDictionary(g => g.Key, g => g.Count());

        return banks
            .OrderBy(b => b.SortKey, StringComparer.Ordinal)
            .Select(b => new TopicBankAdminDto(
                b.Id,
                b.TopicId,
                b.Tier.ToString(),
                b.Difficulty?.ToString(),
                b.Purpose.ToString(),
                b.SortKey,
                countByBank.GetValueOrDefault(b.Id),
                b.CreatedAt))
            .ToList();
    }
}
