namespace FileService.Core;

public sealed class TargetEntityOptions
{
    public IReadOnlyList<string> AllowedTargetEntityTypes { get; init; } = [];
}
