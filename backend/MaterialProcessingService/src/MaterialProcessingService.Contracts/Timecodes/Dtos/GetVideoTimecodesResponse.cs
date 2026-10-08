namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

public sealed record GetVideoTimecodesResponse(
    Guid VideoId,
    Guid? AssetVersion,
    bool HasTranscript,
    TranscriptPreparationDto? TranscriptPreparation,
    TimecodeGenerationDto? Generation,
    ActiveContentGenerationDto? ActiveContentGeneration);
