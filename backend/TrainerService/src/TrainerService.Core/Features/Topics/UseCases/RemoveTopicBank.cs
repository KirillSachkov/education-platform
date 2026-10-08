using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Features.Topics.UseCases;

public sealed record RemoveTopicBankCommand(Guid TopicId, Guid BankId) : ICommand;

public sealed class RemoveTopicBankEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/trainer/topics/{topicId:guid}/banks/{bankId:guid}",
                async Task<EndpointResult> (
                    Guid topicId,
                    Guid bankId,
                    RemoveTopicBankHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new RemoveTopicBankCommand(topicId, bankId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Отвязывает банк вопросов от темы (admin/seed). Сам ECS-квиз не трогается.</summary>
public sealed class RemoveTopicBankHandler : ICommandHandler<RemoveTopicBankCommand>
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITransactionManager _transactions;

    public RemoveTopicBankHandler(ITopicBanksRepository banks, ITransactionManager transactions)
    {
        _banks = banks;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        RemoveTopicBankCommand command,
        CancellationToken cancellationToken)
    {
        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(
            b => b.Id == command.BankId && b.TopicId == command.TopicId,
            cancellationToken);
        if (bankResult.IsFailure)
            return TrainerServiceErrors.Bank.NotFound(command.BankId);

        await _banks.RemoveAsync(bankResult.Value, cancellationToken);

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
