using System.Linq.Expressions;
using Core.Database;
using AuthService.Domain;

namespace AuthService.Core.Database;

/// <summary>
///     Репозиторий профилей пользователей.
///     Writes регистрируют сущность в EF Core контексте; сохранение — через <see cref="ITransactionManager" />.
/// </summary>
public interface IProfileRepository
{
    /// <summary>Добавляет профиль в контекст. Требует SaveChanges.</summary>
    Task AddAsync(UserProfile profile, CancellationToken ct);

    /// <summary>Возвращает первый профиль, удовлетворяющий предикату, или <c>null</c>.</summary>
    Task<UserProfile?> GetByAsync(Expression<Func<UserProfile, bool>> predicate, CancellationToken ct);

    /// <summary>Проверяет наличие профиля по предикату.</summary>
    Task<bool> ExistsAsync(Expression<Func<UserProfile, bool>> predicate, CancellationToken ct);

    /// <summary>Создаёт профиль, если ещё не существует. Идемпотентен, не требует SaveChanges.</summary>
    Task EnsureExistsAsync(Guid userId, CancellationToken ct);
}
