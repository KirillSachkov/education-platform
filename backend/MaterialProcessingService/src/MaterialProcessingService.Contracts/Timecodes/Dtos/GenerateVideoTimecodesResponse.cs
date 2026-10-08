namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

public sealed record GenerateVideoTimecodesResponse(
    Guid JobId,
    Guid VideoId,
    string Status);
