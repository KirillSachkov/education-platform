namespace MaterialProcessingService.Contracts.HttpCommunication;

public sealed record MaterialProcessingServiceOptions
{
    public string Url { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 7;
}
