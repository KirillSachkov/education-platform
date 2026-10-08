using ProgressService.Contracts.Dtos;

namespace ProgressService.Contracts.Responses;

public sealed record GetMaterialViewsCountsResponse(IReadOnlyList<MaterialViewsCountDto> Items);
