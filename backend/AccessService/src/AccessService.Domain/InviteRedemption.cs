using SharedKernel.DomainEvents;

namespace AccessService.Domain;

/// <summary>
/// Аудит-запись о redeem'е инвайт-ссылки. Семантически это child-entity / audit row,
/// а не aggregate root — но в этой кодбазе нет общего <c>Entity&lt;Guid&gt;</c>, поэтому
/// наследуемся от <see cref="AggregateRoot"/> исключительно ради общей формы (Id +
/// EF-friendly ctor). DomainEvents здесь не используются /
/// Audit row recorded on every successful invite redemption. Logically a child
/// entity / pure audit row, not an aggregate root — but the codebase has no shared
/// <c>Entity&lt;Guid&gt;</c> base, so we inherit from <see cref="AggregateRoot"/>
/// purely for the common shape (Id + EF-friendly ctor). DomainEvents are unused here.
/// </summary>
public sealed class InviteRedemption : AggregateRoot
{
    private InviteRedemption() { } // EF

    public InviteRedemption(Guid inviteLinkId, Guid userId, Guid planGrantId, string? ipHash)
    {
        Id = Guid.CreateVersion7();
        InviteLinkId = inviteLinkId;
        UserId = userId;
        PlanGrantId = planGrantId;
        RedeemedAt = DateTimeOffset.UtcNow;
        IpHash = ipHash;
    }

    public Guid Id { get; private set; }

    public Guid InviteLinkId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid PlanGrantId { get; private set; }

    public DateTimeOffset RedeemedAt { get; private set; }

    public string? IpHash { get; private set; }
}
