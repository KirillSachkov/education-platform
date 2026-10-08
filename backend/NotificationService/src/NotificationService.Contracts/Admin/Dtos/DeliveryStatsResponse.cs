namespace NotificationService.Contracts.Admin.Dtos;

/// <summary>
/// Агрегированная статистика доставок за заданный диапазон дат.
/// Все 3 разбивки считаются одним SQL — минимизируем round-trip'ы.
/// </summary>
public sealed record DeliveryStatsResponse
{
    public required IReadOnlyList<ChannelStatusBucket> PerChannel { get; init; }
    public required IReadOnlyList<TypeBucket> PerType { get; init; }
    public required IReadOnlyList<FailureReason> TopFailures { get; init; }
}

public sealed record ChannelStatusBucket
{
    public required short Channel { get; init; }
    public required short Status { get; init; }
    public required long Count { get; init; }
}

public sealed record TypeBucket
{
    public required short Type { get; init; }
    public required long Count { get; init; }
}

public sealed record FailureReason
{
    public required string ErrorCode { get; init; }
    public required long Count { get; init; }
}
