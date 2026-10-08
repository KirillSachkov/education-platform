namespace EducationContentService.Contracts.Materials;

public sealed record GetMaterialSummariesRequest(IReadOnlyCollection<Guid> Ids);
