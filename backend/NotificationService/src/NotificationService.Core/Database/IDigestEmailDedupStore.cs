namespace NotificationService.Core.Database;

/// <summary>
/// Дедуп доставки email по физическому инбоксу для глобального дайджеста. Несколько
/// Identity-аккаунтов могут указывать на один Gmail-инбокс (точки/<c>+tag</c>/регистр
/// <c>RequireUniqueEmail</c> не сворачивает) — без дедупа один дайджест ушёл бы в ящик
/// несколько раз. InApp-запись на каждый user_id остаётся; гасим только физический email.
/// </summary>
public interface IDigestEmailDedupStore
{
    /// <summary>
    /// Атомарно «занимает» пару (correlation прохода, hash инбокса).
    /// <c>true</c> — заняли первыми → письмо нужно отправить;
    /// <c>false</c> — пара уже занята другим аккаунтом этого инбокса → email пропустить.
    /// </summary>
    Task<bool> TryClaimInboxAsync(Guid correlationId, byte[] inboxHash, CancellationToken ct = default);
}
