namespace ProgressService.Contracts.Responses;

public sealed record MaterialViewStatusDto(Guid MaterialId, bool IsViewed, DateTime? ViewedAt);

public sealed record GetMaterialViewStatusResponse(IReadOnlyCollection<MaterialViewStatusDto> Items);
