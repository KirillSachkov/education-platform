using System.Linq.Expressions;
using AccessService.Domain;

namespace AccessService.Core.Database;

public interface IInviteLinksRepository
{
    Task AddAsync(InviteLink invite, CancellationToken ct = default);

    Task<Result<InviteLink, Error>> GetByAsync(
        Expression<Func<InviteLink, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<InviteLink>> GetManyByAsync(
        Expression<Func<InviteLink, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<InviteLink, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Hard-delete invite-link row. Existing <c>plan_grant</c>/<c>invite_redemption</c>
    /// рядом не трогаются — это аудит-данные, инвайт-ссылка просто исчезает из списка.
    /// FK у <c>invite_redemptions.invite_link_id</c> нет — orphan id допустим.
    /// </summary>
    void Remove(InviteLink invite);
}
