using AccessService.Core.Database;
using AccessService.Domain.TgJoinReminders;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Telegram.Events;

namespace AccessService.Core.Features.TgJoinReminders.Handlers;

/// <summary>
///     Wolverine consumer для <see cref="ChatMemberConfirmed"/> (#616, ST-3) — реактивно
///     закрывает трекинг нуджей, когда юзер реально вступил в community-чат плана.
///     Sibling-хендлер к <c>ChatMembershipConfirmedHandler</c> (онбординг TELEGRAM-step):
///     оба слушают одну очередь <c>access.plan_onboarding.telegram_member_events</c>, но
///     обновляют разные агрегаты. Если строки <see cref="TgJoinReminder"/> для (user, plan)
///     нет — silent no-op (этот план не нуджился). Идемпотентен:
///     <see cref="TgJoinReminder.MarkCompleted"/> не сдвигает время при повторе.
/// </summary>
public static class ChatMemberConfirmedTgJoinHandler
{
    public static async Task HandleAsync(
        ChatMemberConfirmed message,
        ITgJoinRemindersRepository reminders,
        ITransactionManager transactions,
        TimeProvider time,
        ILogger<TgJoinReminder> logger,
        CancellationToken ct)
    {
        TgJoinReminder? reminder = await reminders.GetByUserAndPlanAsync(
            message.PlatformUserId, message.PlanId, ct);
        if (reminder is null || reminder.IsCompleted)
        {
            return;
        }

        reminder.MarkCompleted(time.GetUtcNow());
        UnitResult<Error> saveResult = await transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            throw saveResult.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "TgJoin reminder completed reactively: user={UserId} plan={PlanId} after chat-member confirmed",
            message.PlatformUserId, message.PlanId);
    }
}
