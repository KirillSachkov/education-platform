namespace TagService.Contracts.Tags.Requests;

public sealed record GetTagAliasesRequest
{
    public string? Search { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
