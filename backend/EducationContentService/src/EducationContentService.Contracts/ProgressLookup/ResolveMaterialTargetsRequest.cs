namespace EducationContentService.Contracts.ProgressLookup;

public sealed record ResolveMaterialTargetsRequest(
    IReadOnlyCollection<MaterialResolveRequestItem> Items);
