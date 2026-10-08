using System.Linq;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.TgJoinReminders;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Access.Events;
using TelegramBotService.Contracts.HttpCommunication;

namespace AccessService.Core.Features.TgJoinReminders.Handlers;

/// <summary>
///     Wolverine consumer для <see cref="PlanGrantCreated"/> на отдельной очереди
///     <c>access.tg_join_reminder.grant_events</c> (#616, ST-3). Когда юзер получает
///     community-grant на план с привязанными Telegram-чатами, но ещё не вступил —
///     заводит строку трекинга <see cref="TgJoinReminder"/> (idempotent upsert по
///     <c>(user, plan)</c>) и публикует <see cref="TgJoinReminderRequested"/> со стадией
///     <c>INITIAL</c>. Дальнейшие напоминания (REMINDER_1/2) шлёт <c>TgJoinReminderSweeper</c>.
///
///     Гейтинг (зеркалит F1 <c>PlanGrantCreatedTelegramHandler</c>):
///     <list type="number">
///       <item>grant с capability <c>COMMUNITY_ACCESS</c> (из <see cref="PlanGrantCreated.Capabilities"/>);</item>
///       <item>план имеет хотя бы один привязанный Telegram-чат
///         (<see cref="ITelegramBotServiceClient.HasActiveChatBindingAsync"/>);</item>
///       <item><see cref="PlanGrantCreated.Source"/> != MIGRATION (бэкфилл не нуджим);</item>
///       <item>не self-grant (<see cref="PlanGrantCreated.UserId"/> != автор плана).</item>
///     </list>
///     Идемпотентность: повторный grant того же плана не пере-создаёт строку
///     (unique <c>(user, plan)</c> + явный exists-чек). Если строка уже есть — INITIAL не
///     дублируется. Soft-degrade: TBS down → no-op (нуджить «вступи в группу» бессмысленно,
///     пока неизвестно, есть ли у плана группа).
/// </summary>
public static class PlanGrantCreatedTgJoinHandler
{
    private const string CAP_COMMUNITY_ACCESS = "COMMUNITY_ACCESS";

    public static async Task HandleAsync(
        PlanGrantCreated message,
        ITgJoinRemindersRepository reminders,
        ITelegramBotServiceClient telegram,
        IOutboxService outbox,
        ITransactionManager transactions,
        TimeProvider time,
        ILogger<TgJoinReminder> logger,
        CancellationToken ct)
    {
        // 1) community-grant?
        bool hasCommunityAccess = message.Capabilities is { Count: > 0 }
            && message.Capabilities.Contains(CAP_COMMUNITY_ACCESS, StringComparer.Ordinal);
        if (!hasCommunityAccess)
        {
            return;
        }

        // 3) бэкфилл не нуджим.
        if (string.Equals(message.Source, nameof(PlanGrantSource.MIGRATION), StringComparison.Ordinal))
        {
            return;
        }

        // 4) self-grant (автор сам себе) — не нуджим.
        if (message.UserId == message.PlanAuthorId)
        {
            return;
        }

        // Идемпотентность: строка уже заведена для (user, plan) → INITIAL уже отправлен.
        bool exists = await reminders.ExistsAsync(
            r => r.UserId == message.UserId && r.PlanId == message.PlanId, ct);
        if (exists)
        {
            return;
        }

        // 2) у плана есть привязанные TG-чаты? Soft-degrade на сбое TBS.
        Result<bool, Error> hasChats = await telegram.HasActiveChatBindingAsync(message.PlanId, ct);
        if (hasChats.IsFailure)
        {
            logger.LogWarning(
                "TgJoin INITIAL skipped: HasActiveChatBinding failed for plan={PlanId} (user={UserId}): {Error}",
                message.PlanId, message.UserId, hasChats.Error.Type);
            return;
        }

        if (!hasChats.Value)
        {
            return; // у плана нет community-чатов → нечего нуджить.
        }

        DateTimeOffset now = time.GetUtcNow();
        TgJoinReminder reminder = TgJoinReminder.Create(
            message.UserId, message.PlanId, message.GrantId, now);

        await reminders.AddAsync(reminder, ct);
        await outbox.PublishAsync(new TgJoinReminderRequested(
            message.UserId,
            message.PlanId,
            message.GrantId,
            message.PlanName ?? string.Empty,
            TgJoinReminderStages.Initial,
            now));
        UnitResult<Error> saveResult = await transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            throw saveResult.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "TgJoin INITIAL nudge: user={UserId} plan={PlanId} grant={GrantId}",
            message.UserId, message.PlanId, message.GrantId);
    }
}
