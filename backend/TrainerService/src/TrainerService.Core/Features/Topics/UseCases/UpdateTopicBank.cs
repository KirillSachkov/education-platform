using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Topics.UseCases;

public sealed record UpdateTopicBankCommand(
    Guid BankId,
    string? Tier,
    string? Difficulty,
    string? Purpose) : ICommand;

public sealed class UpdateTopicBankEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/trainer/topic-banks/{bankId:guid}",
                async Task<EndpointResult<TopicBankIdResponse>> (
                    Guid bankId,
                    UpdateTopicBankRequest request,
                    UpdateTopicBankHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new UpdateTopicBankCommand(bankId, request.Tier, request.Difficulty, request.Purpose),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Обновляет банк вопросов (admin): Tier (FREE/PAID), Difficulty (опц.), Purpose (STUDY/MOCK).
///     TopicId immutable — банк не переносится между темами. SortKey сохраняется как есть —
///     порядок меняется отдельным переупорядочиванием.
/// </summary>
public sealed class UpdateTopicBankHandler : ICommandHandler<TopicBankIdResponse, UpdateTopicBankCommand>
{
    private const string DEFAULT_TIER = "FREE";

    private readonly ITopicBanksRepository _banks;
    private readonly ITransactionManager _transactions;

    public UpdateTopicBankHandler(ITopicBanksRepository banks, ITransactionManager transactions)
    {
        _banks = banks;
        _transactions = transactions;
    }

    public async Task<Result<TopicBankIdResponse, Error>> Handle(
        UpdateTopicBankCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryParseTier(command.Tier, out BankTier tier))
            return TrainerServiceErrors.Bank.InvalidTier(command.Tier!);

        Result<QuestionDifficulty?, Error> difficultyResult = ParseDifficulty(command.Difficulty);
        if (difficultyResult.IsFailure)
            return difficultyResult.Error;

        if (!TryParsePurpose(command.Purpose, out BankPurpose purpose))
            return TrainerServiceErrors.Bank.InvalidPurpose(command.Purpose ?? string.Empty);

        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(b => b.Id == command.BankId, cancellationToken);
        if (bankResult.IsFailure)
            return TrainerServiceErrors.Bank.NotFound(command.BankId);

        TopicBank bank = bankResult.Value;
        bank.Update(tier, difficultyResult.Value, purpose, bank.SortKey);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new TopicBankIdResponse(bank.Id);
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

    private static bool TryParsePurpose(string? raw, out BankPurpose purpose)
    {
        // Default STUDY when omitted — matches the create-bank default.
        if (string.IsNullOrWhiteSpace(raw))
        {
            purpose = BankPurpose.STUDY;
            return true;
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: false, out purpose) && Enum.IsDefined(purpose);
    }
}
