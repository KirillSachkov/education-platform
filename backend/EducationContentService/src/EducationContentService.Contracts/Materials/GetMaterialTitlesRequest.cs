namespace EducationContentService.Contracts.Materials;

public sealed record GetMaterialTitlesRequest(IReadOnlyCollection<Guid> Ids);
