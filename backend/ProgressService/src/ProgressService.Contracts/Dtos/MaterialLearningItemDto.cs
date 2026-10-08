namespace ProgressService.Contracts.Dtos;

public sealed record MaterialLearningItemDto(
    Guid MaterialId,
    string Status,
    DateTime? ViewedAt);
