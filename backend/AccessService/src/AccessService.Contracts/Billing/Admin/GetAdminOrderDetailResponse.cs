namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Detail view: order + chronological audit-log.
/// </summary>
public sealed record GetAdminOrderDetailResponse(
    AdminOrderDetail Order,
    IReadOnlyList<AdminOrderEventDto> Events);
