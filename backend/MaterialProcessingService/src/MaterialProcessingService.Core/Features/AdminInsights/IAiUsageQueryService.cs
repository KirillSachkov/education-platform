namespace MaterialProcessingService.Core.Features.AdminInsights;

public interface IAiUsageQueryService
{
    Task<AiUsageSnapshot> GetUsageAsync(DateTime sinceUtc, CancellationToken cancellationToken = default);
}

public sealed record AiUsageSnapshot(
    int TranscriptsCreated,
    IReadOnlyList<AiUsageRowDto> Rows);
