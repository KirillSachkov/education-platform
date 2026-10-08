namespace Shared.Messaging.IntegrationEvents.Progress.Events;

/// <summary>
/// Published when a user's gamification level increases (e.g. L4 → L5) as a result of an XP award.
/// Only a genuine upward transition emits this — same-level awards and XP revokes do not (#555).
/// Recipient of the resulting notification: the user themselves (<paramref name="UserId"/>).
/// </summary>
public sealed record UserLeveledUp(
    Guid UserId,
    int PreviousLevel,
    int NewLevel,
    int TotalXp);
