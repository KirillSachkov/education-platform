using System.Linq.Expressions;
using AccessService.Domain;

namespace AccessService.Core.Database;

public interface IPlansRepository
{
    Task AddAsync(Plan plan, CancellationToken ct = default);

    Task<Result<Plan, Error>> GetByAsync(
        Expression<Func<Plan, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<Plan>> GetManyByAsync(
        Expression<Func<Plan, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<Plan, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Помечает план на удаление (EF <c>Remove</c>). На <c>SaveChanges</c> EF удалит строку
    /// <c>plans</c>, а DB ON DELETE CASCADE добьёт дочерние <c>plan_courses</c> и
    /// <c>plan_onboarding_flows</c> (+ steps). Прочие ссылающиеся таблицы без FK на
    /// <c>plans</c> чистятся явно в use-case'е hard-delete.
    /// </summary>
    void Remove(Plan plan);
}
