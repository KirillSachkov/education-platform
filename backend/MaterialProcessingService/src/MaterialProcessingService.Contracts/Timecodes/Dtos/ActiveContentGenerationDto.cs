namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

public sealed record ActiveContentGenerationDto(
    Guid JobId,
    Guid MaterialId,
    string Status,
    string Stage,
    int ProgressPercent,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime RequestedAt);
