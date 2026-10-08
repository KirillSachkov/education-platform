namespace TagService.Contracts.HttpCommunication;

public record TagServiceOptions
{
    public string Url { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 7;
}
