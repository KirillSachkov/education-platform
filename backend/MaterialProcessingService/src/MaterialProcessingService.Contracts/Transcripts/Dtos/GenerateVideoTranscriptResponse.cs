namespace MaterialProcessingService.Contracts.Transcripts.Dtos;

public sealed record GenerateVideoTranscriptResponse(
    Guid JobId,
    Guid VideoId,
    string Status);
