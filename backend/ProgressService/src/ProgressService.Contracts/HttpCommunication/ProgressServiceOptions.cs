namespace ProgressService.Contracts.HttpCommunication;

public sealed class ProgressServiceOptions
{
    public string Url { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 30;
}
