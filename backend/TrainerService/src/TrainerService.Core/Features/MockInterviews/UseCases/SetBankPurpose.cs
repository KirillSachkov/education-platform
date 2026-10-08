using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.MockInterviews.UseCases;

public sealed record SetBankPurposeCommand(Guid BankId, string? Purpose) : ICommand;

public sealed class SetBankPurposeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("/trainer/topic-banks/{bankId:guid}/purpose",
                async Task<EndpointResult<TopicBankIdResponse>> (
                    Guid bankId,
                    SetBankPurposeRequest request,
                    SetBankPurposeHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new SetBankPurposeCommand(bankId, request.Purpose), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Переключает назначение банка вопросов (admin): STUDY ↔ MOCK. Позволяет пометить
///     существующие «собесные» банки как MOCK, чтобы они не раздували учебный список темы,
///     но попадали в пул mock-собеса. #568.
/// </summary>
public sealed class SetBankPurposeHandler : ICommandHandler<TopicBankIdResponse, SetBankPurposeCommand>
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITransactionManager _transactions;

    public SetBankPurposeHandler(ITopicBanksRepository banks, ITransactionManager transactions)
    {
        _banks = banks;
        _transactions = transactions;
    }

    public async Task<Result<TopicBankIdResponse, Error>> Handle(
        SetBankPurposeCommand command,
        CancellationToken cancellationToken)
    {
        if (!TryParsePurpose(command.Purpose, out BankPurpose purpose))
            return TrainerServiceErrors.Bank.InvalidPurpose(command.Purpose ?? string.Empty);

        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(b => b.Id == command.BankId, cancellationToken);
        if (bankResult.IsFailure)
            return TrainerServiceErrors.Bank.NotFound(command.BankId);

        TopicBank bank = bankResult.Value;
        bank.SetPurpose(purpose);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        return new TopicBankIdResponse(bank.Id);
    }

    private static bool TryParsePurpose(string? raw, out BankPurpose purpose)
    {
        purpose = default;
        return !string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw.Trim(), ignoreCase: false, out purpose)
            && Enum.IsDefined(purpose);
    }
}
