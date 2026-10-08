namespace AccessService.Contracts.Plans.Requests;

/// <summary>
///     Bulk-reorder планов в author/admin UI. Каждый item задаёт
///     <see cref="PlanOrderItem.DisplayOrder"/> для plan'а с <see cref="PlanOrderItem.PlanId"/>.
///     Обычный author может менять только свои планы; owner/admin — планы платформы.
/// </summary>
public sealed record ReorderPlansRequest(IReadOnlyList<PlanOrderItem> Orders);

public sealed record PlanOrderItem(Guid PlanId, int DisplayOrder);
