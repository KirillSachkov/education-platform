namespace AuthService.Contracts;

public record CursorResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; } = [];
    public required string? NextCursor { get; init; }
    public required long TotalCount { get; init; }
}
