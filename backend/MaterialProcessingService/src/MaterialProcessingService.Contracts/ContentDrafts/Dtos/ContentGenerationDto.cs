namespace MaterialProcessingService.Contracts.ContentDrafts.Dtos;

public sealed record ContentGenerationDto(
    Guid JobId,
    string Status,
    string Stage,
    int ProgressPercent,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt);
