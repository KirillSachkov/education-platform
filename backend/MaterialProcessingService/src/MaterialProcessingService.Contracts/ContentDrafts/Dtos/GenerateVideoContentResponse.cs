namespace MaterialProcessingService.Contracts.ContentDrafts.Dtos;

public sealed record GenerateVideoContentResponse(
    Guid JobId,
    Guid VideoId,
    Guid MaterialId,
    string Status);
