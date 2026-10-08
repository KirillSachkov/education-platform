namespace EducationContentService.Contracts.Materials;

public sealed record GetMaterialsCardMetaRequest(IReadOnlyCollection<Guid> Ids);
