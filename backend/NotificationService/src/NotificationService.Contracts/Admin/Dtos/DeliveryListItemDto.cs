namespace NotificationService.Contracts.Admin.Dtos;

/// <summary>
/// Один элемент в списке доставок для admin-панели.
/// Flat JOIN записей <c>notification_deliveries</c> + базовые поля notification'а (type,
/// recipient) — чтобы не делать N+1 lookup'ов.
/// </summary>
public sealed record DeliveryListItemDto
{
    public required Guid DeliveryId { get; init; }
    public required Guid NotificationId { get; init; }
    public required Guid RecipientUserId { get; init; }
    public required short Type { get; init; }
    public required string TemplateId { get; init; }
    public required short Channel { get; init; }
    public required short Status { get; init; }
    public string? ProviderMessageId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorDetail { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
}
