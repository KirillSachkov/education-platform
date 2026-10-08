namespace SearchService.Infrastructure.Typesense;

public record TypesenseOptions
{
    public string ApiKey { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}
