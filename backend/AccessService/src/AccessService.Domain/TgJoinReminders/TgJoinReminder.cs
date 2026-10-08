namespace AccessService.Domain.TgJoinReminders;

/// <summary>
///     Aggregate root: трекинг нуджей «вступи в Telegram-группу плана» для одного
///     <c>(user, plan)</c> (#616). Создаётся on-grant'ом, когда у плана есть привязанные
///     community-чаты и юзер ещё не вступил. <see cref="TgJoinReminderSweeper"/> по возрасту
///     строки шлёт REMINDER_1 (≈ T+2д) и REMINDER_2 (≈ T+9д); после второго — стоп.
///     Реактивно закрывается (<see cref="MarkCompleted"/>), когда приходит подтверждённое
///     членство (<c>chat_member.confirmed</c>) либо sweeper'ская membership-проверка вернула
///     «уже состоит».
///
///     PK генерируется в factory (<see cref="Guid.CreateVersion7"/>) — aggregate root
///     сохраняется через <c>DbSet.AddAsync</c>, не через nav-collection, поэтому заранее
///     заполненный PK безопасен (см. backend-transactions.md правило 4).
/// </summary>
public sealed class TgJoinReminder
{
    private TgJoinReminder() { } // EF

    private TgJoinReminder(
        Guid id,
        Guid userId,
        Guid planId,
        Guid grantId,
        DateTimeOffset createdAt)
    {
        Id = id;
        UserId = userId;
        PlanId = planId;
        GrantId = grantId;
        RemindersSent = 0;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PlanId { get; private set; }

    /// <summary>Grant, давший community-доступ — используется как idempotency-scope в событии.</summary>
    public Guid GrantId { get; private set; }

    /// <summary>Сколько напоминаний уже отправлено (0..2). 0 = только INITIAL на момент создания.</summary>
    public int RemindersSent { get; private set; }

    /// <summary>Когда отправлен последний reminder. null = ни одного (REMINDER_*) ещё не было.</summary>
    public DateTimeOffset? LastRemindedAt { get; private set; }

    /// <summary>Когда юзер вступил (членство подтверждено). null = ещё активна (нуджим).</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Якорь возраста строки — момент выдачи гранта. По нему sweeper считает стадию.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary><c>true</c> если юзер уже вступил — sweeper такие строки пропускает.</summary>
    public bool IsCompleted => CompletedAt is not null;

    /// <summary>
    ///     Создаёт строку трекинга для пары <c>(user, plan)</c>. <paramref name="createdAt"/> ≈
    ///     момент выдачи гранта — sweeper считает возраст строки от него.
    /// </summary>
    public static TgJoinReminder Create(
        Guid userId,
        Guid planId,
        Guid grantId,
        DateTimeOffset createdAt) =>
        new(Guid.CreateVersion7(), userId, planId, grantId, createdAt);

    /// <summary>Фиксирует отправку очередного reminder'а: <c>RemindersSent++</c> + <see cref="LastRemindedAt"/>.</summary>
    public void MarkReminded(DateTimeOffset at)
    {
        RemindersSent++;
        LastRemindedAt = at;
    }

    /// <summary>Закрывает трекинг — юзер вступил. Идемпотентно (повторный вызов не сдвигает время).</summary>
    public void MarkCompleted(DateTimeOffset at)
    {
        if (CompletedAt is null)
        {
            CompletedAt = at;
        }
    }
}
