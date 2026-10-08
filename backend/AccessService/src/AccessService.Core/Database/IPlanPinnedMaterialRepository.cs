using AccessService.Domain.HomePins;

namespace AccessService.Core.Database;

/// <summary>
///     Репозиторий закрепов материалов на home-дашборде плана (epic #397).
///     Flat aggregate root — persist через <see cref="Core.Database.ITransactionManager"/>,
///     метода <c>SaveChangesAsync</c> на репозитории нет (backend-transactions.md правило 2).
/// </summary>
public interface IPlanPinnedMaterialRepository
{
    Task AddAsync(PlanPinnedMaterial pin, CancellationToken ct = default);

    /// <summary>Закрепы плана, упорядоченные по <c>SortKey</c> ASC.</summary>
    Task<IReadOnlyList<PlanPinnedMaterial>> GetByPlanAsync(Guid planId, CancellationToken ct = default);

    Task<Result<PlanPinnedMaterial, Error>> GetByIdAsync(Guid pinId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid planId, Guid materialId, CancellationToken ct = default);

    /// <summary>Закрепы нескольких планов, упорядоченные по <c>(PlanId, SortKey ASC)</c>.</summary>
    Task<IReadOnlyList<PlanPinnedMaterial>> GetManyByPlansAsync(
        IEnumerable<Guid> planIds,
        CancellationToken ct = default);

    void Remove(PlanPinnedMaterial pin);
}
