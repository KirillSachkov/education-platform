namespace NotificationService.Contracts.Admin.Dtos;

public sealed record DeliveryListResponse
{
    public required IReadOnlyList<DeliveryListItemDto> Items { get; init; }
    public DateTimeOffset? NextCursorBefore { get; init; }
    public Guid? NextCursorId { get; init; }
}
