namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

public sealed record TranscriptPreparationDto(
    Guid JobId,
    string Source,
    string Status,
    string Stage,
    int ProgressPercent,
    Guid? MaterialId,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime RequestedAt);
