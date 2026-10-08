using AuthService.Domain;

namespace AuthService.Core.Database;

/// <summary>
///     Persistence согласий пользователя на юр-документы.
///     Юр-доказательство при споре по 152-ФЗ / ЗоЗПП.
/// </summary>
public interface IConsentRepository
{
    /// <summary>Сохранить факт согласия (без commit, требует SaveChangesAsync).</summary>
    Task AddAsync(UserConsent consent, CancellationToken ct);

    /// <summary>Все согласия пользователя, сортировка от свежих к старым.</summary>
    Task<IReadOnlyList<UserConsent>> GetByUserAsync(Guid userId, CancellationToken ct);
}
