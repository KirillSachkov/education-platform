using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Messaging.Consumers;

/// <summary>
///     На <c>plan.hard_deleted</c> — план полностью удалён в AccessService, поэтому отвязываем
///     все его chat-binding'и: best-effort revoke invite-link + удаление строки (иначе binding
///     осиротеет, ссылаясь на несуществующий план). Событие приходит только для «пустого» плана
///     (без активных грантов), поэтому членов кикать не нужно — F6 auto-kick на это не подписан.
///     Идемпотентно: нет binding'ов → no-op; повторная доставка находит уже удалённые строки.
///     Advisory lock и транзакция сериализуют hard-delete с bind/unbind одного плана;
///     bulk-delete выполняется через <see cref="IChatBindingRepository.RemoveByPlanIdAsync"/>.
/// </summary>
public sealed class PlanHardDeletedTelegramHandler
{
    private readonly IChatBindingRepository _bindings;
    private readonly IChatAdministrationApi _chatApi;
    private readonly ITransactionManager _transactions;
    private readonly ILogger<PlanHardDeletedTelegramHandler> _logger;

    public PlanHardDeletedTelegramHandler(
        IChatBindingRepository bindings,
        IChatAdministrationApi chatApi,
        ITransactionManager transactions,
        ILogger<PlanHardDeletedTelegramHandler> logger)
    {
        _bindings = bindings;
        _chatApi = chatApi;
        _transactions = transactions;
        _logger = logger;
    }

    public async Task Handle(PlanHardDeleted evt, CancellationToken cancellationToken)
    {
        UnitResult<Error> beginResult = await _transactions.BeginTransactionAsync(cancellationToken);
        if (beginResult.IsFailure)
            throw beginResult.Error.AsTransient().ToException();

        await _bindings.AcquirePlanMutationLockAsync(evt.PlanId, cancellationToken);

        IReadOnlyList<ChatBinding> bound = await _bindings.GetManyByAsync(
            b => b.PlanId == evt.PlanId, cancellationToken);

        if (bound.Count == 0)
        {
            UnitResult<Error> emptyCommit = await _transactions.CommitTransactionAsync(cancellationToken);
            if (emptyCommit.IsFailure)
                throw emptyCommit.Error.AsTransient().ToException();
            return;
        }

        foreach (ChatBinding binding in bound)
        {
            // Best-effort: revoke invite link (чат мог быть удалён / бот выкинут — не блокер).
            await _chatApi.RevokeChatInviteLinkAsync(
                binding.TelegramChatId, binding.InviteLink, cancellationToken);
        }

        await _bindings.RemoveByPlanIdAsync(evt.PlanId, cancellationToken);

        UnitResult<Error> commitResult = await _transactions.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            throw commitResult.Error.AsTransient().ToException();

        _logger.LogInformation(
            "Removed {Count} chat-binding(s) for hard-deleted plan {PlanId}",
            bound.Count, evt.PlanId);
    }
}
