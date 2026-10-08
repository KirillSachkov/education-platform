using AccessService.Core.Database;
using AccessService.Domain.HomePins;

namespace AccessService.Infrastructure.Postgres;

internal sealed class PlanPinnedMaterialRepository : IPlanPinnedMaterialRepository
{
    private readonly AccessServiceDbContext _db;

    public PlanPinnedMaterialRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(PlanPinnedMaterial pin, CancellationToken ct = default) =>
        await _db.PlanPinnedMaterials.AddAsync(pin, ct);

    public async Task<IReadOnlyList<PlanPinnedMaterial>> GetByPlanAsync(
        Guid planId,
        CancellationToken ct = default)
    {
        List<PlanPinnedMaterial> pins = await _db.PlanPinnedMaterials
            .Where(p => p.PlanId == planId)
            .ToListAsync(ct);

        // SortKey хранится value-converted string'ом — сортировка по base-62 ключу
        // лексикографически в памяти (тот же приём, что у PlanOnboardingFlow.Steps).
        return pins
            .OrderBy(p => p.SortKey.Value, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<Result<PlanPinnedMaterial, Error>> GetByIdAsync(
        Guid pinId,
        CancellationToken ct = default)
    {
        PlanPinnedMaterial? pin = await _db.PlanPinnedMaterials
            .FirstOrDefaultAsync(p => p.Id == pinId, ct);
        return pin is null ? HomePinErrors.PinNotFound() : pin;
    }

    public async Task<bool> ExistsAsync(Guid planId, Guid materialId, CancellationToken ct = default) =>
        await _db.PlanPinnedMaterials.AnyAsync(p => p.PlanId == planId && p.MaterialId == materialId, ct);

    public async Task<IReadOnlyList<PlanPinnedMaterial>> GetManyByPlansAsync(
        IEnumerable<Guid> planIds,
        CancellationToken ct = default)
    {
        Guid[] ids = planIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        List<PlanPinnedMaterial> pins = await _db.PlanPinnedMaterials
            .Where(p => ids.Contains(p.PlanId))
            .ToListAsync(ct);

        return pins
            .OrderBy(p => p.PlanId)
            .ThenBy(p => p.SortKey.Value, StringComparer.Ordinal)
            .ToList();
    }

    public void Remove(PlanPinnedMaterial pin) => _db.PlanPinnedMaterials.Remove(pin);
}
