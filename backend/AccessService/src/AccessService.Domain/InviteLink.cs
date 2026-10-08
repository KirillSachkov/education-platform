using SharedKernel.DomainEvents;

namespace AccessService.Domain;

/// <summary>
/// Aggregate root: инвайт-ссылка для выдачи плана доступа /
/// Aggregate root: invite link that grants a plan when redeemed.
/// </summary>
public sealed class InviteLink : AggregateRoot
{
    private InviteLink() { } // EF

    private InviteLink(
        Guid id,
        Guid planId,
        InviteToken token,
        Guid createdBy,
        bool multiUse,
        int? maxUses,
        DateTimeOffset? expiresAt,
        string? label,
        DateTimeOffset createdAt)
    {
        Id = id;
        PlanId = planId;
        Token = token;
        CreatedBy = createdBy;
        MultiUse = multiUse;
        MaxUses = maxUses;
        ExpiresAt = expiresAt;
        Label = label;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public InviteToken Token { get; private set; } = null!;

    public Guid CreatedBy { get; private set; }

    public bool MultiUse { get; private set; }

    public int? MaxUses { get; private set; }

    public int UsageCount { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public bool IsActive { get; private set; }

    public string? Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>
    /// Создаёт инвайт-ссылку. Все инварианты на параметры (например maxUses>0)
    /// проверяются в use-case, не здесь — фабрика принимает уже валидные значения /
    /// Creates an invite link. Parameter-level invariants are enforced by the
    /// use-case layer; this factory takes already-validated input.
    /// </summary>
    public static InviteLink Create(
        Guid planId,
        Guid createdBy,
        bool multiUse,
        int? maxUses,
        DateTimeOffset? expiresAt,
        string? label) =>
        new(
            Guid.CreateVersion7(),
            planId,
            InviteToken.Generate(),
            createdBy,
            multiUse,
            maxUses,
            expiresAt,
            label,
            DateTimeOffset.UtcNow);

    /// <summary>
    /// Проверяет, что ссылку можно redeem'нуть прямо сейчас /
    /// Checks that the link is redeemable at <paramref name="now"/>.
    /// </summary>
    public UnitResult<Error> ValidateForRedeem(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return AccessErrors.InviteRevoked();
        }

        if (ExpiresAt is not null && ExpiresAt.Value < now)
        {
            return AccessErrors.InviteExpired();
        }

        if (MaxUses is not null && UsageCount >= MaxUses.Value)
        {
            return AccessErrors.InviteUsageExhausted();
        }

        if (!MultiUse && UsageCount >= 1)
        {
            return AccessErrors.InviteUsageExhausted();
        }

        return UnitResult.Success<Error>();
    }

    public void RegisterUsage() => UsageCount++;

    public void Revoke()
    {
        IsActive = false;
        RevokedAt = DateTimeOffset.UtcNow;
    }
}
