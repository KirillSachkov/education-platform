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

public sealed record DeleteTopicBankCommand(Guid BankId) : ICommand;

public sealed class DeleteTopicBankEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/trainer/topic-banks/{bankId:guid}",
                async Task<EndpointResult> (
                    Guid bankId,
                    DeleteTopicBankHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new DeleteTopicBankCommand(bankId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Удаляет банк вопросов по id (admin) — flat-маршрут для MCP/редактора, без указания темы
///     (зеркалит flat update/tier-роуты). Сам ECS-квиз не трогается. Для удаления в контексте темы
///     остаётся <c>DELETE /trainer/topics/{topicId}/banks/{bankId}</c>.
/// </summary>
public sealed class DeleteTopicBankHandler : ICommandHandler<DeleteTopicBankCommand>
{
    private readonly ITopicBanksRepository _banks;
    private readonly ITransactionManager _transactions;

    public DeleteTopicBankHandler(ITopicBanksRepository banks, ITransactionManager transactions)
    {
        _banks = banks;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        DeleteTopicBankCommand command,
        CancellationToken cancellationToken)
    {
        Result<TopicBank, Error> bankResult = await _banks.GetByAsync(b => b.Id == command.BankId, cancellationToken);
        if (bankResult.IsFailure)
            return TrainerServiceErrors.Bank.NotFound(command.BankId);

        await _banks.RemoveAsync(bankResult.Value, cancellationToken);

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
