using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.UseCases;

public sealed record AddTopicBankCommand(
    Guid TopicId,
    string? Tier,
    string? Difficulty) : ICommand;

public sealed class AddTopicBankEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/topics/{topicId:guid}/banks",
                async Task<EndpointResult<TopicBankIdResponse>> (
                    Guid topicId,
                    AddTopicBankRequest request,
                    AddTopicBankHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new AddTopicBankCommand(topicId, request.Tier, request.Difficulty),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Создаёт пустой банк вопросов под темой (admin/seed, #623). Вопросы добавляются отдельно через
///     question CRUD (<c>POST /trainer/topic-banks/{bankId}/questions</c>). Tier по умолчанию FREE;
///     difficulty опционально; назначение STUDY.
/// </summary>
public sealed class AddTopicBankHandler : ICommandHandler<TopicBankIdResponse, AddTopicBankCommand>
{
    private const string DEFAULT_TIER = "FREE";

    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;
    private readonly ITransactionManager _transactions;

    public AddTopicBankHandler(
        ITopicsRepository topics,
        ITopicBanksRepository banks,
        ITransactionManager transactions)
    {
        _topics = topics;
        _banks = banks;
        _transactions = transactions;
    }

    public async Task<Result<TopicBankIdResponse, Error>> Handle(
        AddTopicBankCommand command,
        CancellationToken cancellationToken)
    {
        Result<Topic, Error> topicResult = await _topics.GetByAsync(t => t.Id == command.TopicId, cancellationToken);
        if (topicResult.IsFailure)
            return TrainerServiceErrors.Topic.NotFound(command.TopicId);

        if (!TryParseTier(command.Tier, out BankTier tier))
            return TrainerServiceErrors.Bank.InvalidTier(command.Tier!);

        Result<QuestionDifficulty?, Error> difficultyResult = ParseDifficulty(command.Difficulty);
        if (difficultyResult.IsFailure)
            return difficultyResult.Error;

        string sortKey = await ComputeAppendSortKeyAsync(command.TopicId, cancellationToken);

        Result<TopicBank, Error> bankResult = TopicBank.Create(
            command.TopicId,
            tier,
            difficultyResult.Value,
            sortKey,
            BankPurpose.STUDY);
        if (bankResult.IsFailure)
            return bankResult.Error;

        await _banks.AddAsync(bankResult.Value, cancellationToken);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new TopicBankIdResponse(bankResult.Value.Id);
    }

    private static bool TryParseTier(string? raw, out BankTier tier)
    {
        string value = string.IsNullOrWhiteSpace(raw) ? DEFAULT_TIER : raw.Trim();
        return Enum.TryParse(value, ignoreCase: false, out tier) && Enum.IsDefined(tier);
    }

    private static Result<QuestionDifficulty?, Error> ParseDifficulty(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (QuestionDifficulty?)null;

        if (Enum.TryParse(raw.Trim(), ignoreCase: false, out QuestionDifficulty parsed) && Enum.IsDefined(parsed))
            return parsed;

        return TrainerServiceErrors.Bank.InvalidDifficulty(raw);
    }

    private async Task<string> ComputeAppendSortKeyAsync(Guid topicId, CancellationToken ct)
    {
        string? maxKey = await _banks.GetMaxSortKeyAsync(topicId, ct);
        if (maxKey is null)
            return SortKey.Initial().Value;

        Result<SortKey, Error> parsed = SortKey.Create(maxKey);
        return parsed.IsSuccess ? SortKey.After(parsed.Value).Value : SortKey.Initial().Value;
    }
}
