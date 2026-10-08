namespace NotificationService.Core.Notifications;

/// <summary>
/// Утилиты для стабильных correlation id / Stable correlation-id helpers.
///
/// Идемпотентность <c>Notification</c> строится на unique-индексе
/// <c>(correlation_id, recipient_user_id, type)</c> — один retry события не создаст дубликат.
/// Для одиночного получателя хватает primary id из event'а (<c>UserId</c>, <c>SubmissionId</c>,
/// <c>MaterialId</c>); для fan-out на множество получателей нужна комбинация
/// (<c>broadcastId × recipient</c>), чтобы разные получатели не блокировали друг друга.
/// </summary>
public static class CorrelationIds
{
    /// <summary>
    /// Детерминированный id из пары Guid (XOR по байтам). <b>Симметричен</b>:
    /// <c>Combine(a, b) == Combine(b, a)</c>. Это значит, что в текущих use case'ах нельзя
    /// использовать одну и ту же пару Guid-доменов с разным порядком —
    /// в текущей кодовой базе все callers используют разные namespace'ы:
    /// <c>(userId, courseId)</c>, <c>(recipientId, materialId)</c>, <c>(broadcastId, recipientId)</c> —
    /// collision невозможен.
    ///
    /// При добавлении нового сценария — проверь, что пара уникальна. Если нужен order-aware id,
    /// мигрировать функцию на UUID v5 (<c>SHA1(namespace || a || b)</c>) — но это сломает
    /// идемпотентность для in-flight retry'ев на момент deploy'а.
    /// </summary>
    public static Guid Combine(Guid a, Guid b)
    {
        Span<byte> buf = stackalloc byte[16];
        Span<byte> bufA = stackalloc byte[16];
        Span<byte> bufB = stackalloc byte[16];
        a.TryWriteBytes(bufA);
        b.TryWriteBytes(bufB);
        for (int i = 0; i < 16; i++)
            buf[i] = (byte)(bufA[i] ^ bufB[i]);
        return new Guid(buf);
    }

    public static Guid Combine(Guid a, Guid b, Guid c) =>
        Combine(Combine(a, b), c);
}
