namespace EducationContentService.Contracts.Materials;

public sealed record UpdateMaterialContentRequest(
    Guid GenerationJobId,
    Guid VideoId,
    Guid AssetVersion,
    string Markdown);
