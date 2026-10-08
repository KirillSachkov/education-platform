namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Полные поля <see cref="Domain.Order"/> для admin-detail view.
/// </summary>
public sealed record AdminOrderDetail(
    Guid OrderId,
    Guid UserId,
    Guid PlanId,
    long AmountCents,
    string Currency,
    string Status,
    string? Provider,
    string? ExternalProviderRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? FailureReason,
    string? CorrelationId);
