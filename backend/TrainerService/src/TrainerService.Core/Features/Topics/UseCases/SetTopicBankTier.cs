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

public sealed record SetTopicBankTierCommand(Guid BankId, string? Tier) : ICommand;

public sealed class SetTopicBankTierEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/trainer/topic-banks/{bankId:guid}/tier",
                async Task<EndpointResult<TopicBankIdResponse>> (
                    Guid bankId,
                    SetTopicBankTierRequest request,
                    SetTopicBankTierHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new SetTopicBankTierCommand(bankId, request.Tier), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Переключает фримиум-tier банка (admin): FREE ↔ PAID. Удобная точка для admin/MCP-тоггла,
///     не таская весь Update-payload. Зеркалит <c>SetBankPurpose</c>.
/// </summary>
public sealed class SetTopicBankTierHandler : ICommandHandler<TopicBankIdResponse, SetTopicBankTierCommand>
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITransactionManager _transactions;

    public SetTopicBankTierHandler(ITopicBanksRepository banks, ITransactionManager transactions)
    {
        _banks = banks;
        _transactions = transactions;
    }

    public async Task<Result<TopicBankIdResponse, Error>> Handle(
        SetTopicBankTierCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryParseTier(command.Tier, out BankTier tier))
            return TrainerServiceErrors.Bank.InvalidTier(command.Tier ?? string.Empty);

        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(b => b.Id == command.BankId, cancellationToken);
        if (bankResult.IsFailure)
            return TrainerServiceErrors.Bank.NotFound(command.BankId);

        TopicBank bank = bankResult.Value;
        bank.SetTier(tier);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new TopicBankIdResponse(bank.Id);
    }

    private static bool TryParseTier(string? raw, out BankTier tier)
    {
        tier = default;
        return !string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw.Trim(), ignoreCase: false, out tier)
            && Enum.IsDefined(tier);
    }
}
