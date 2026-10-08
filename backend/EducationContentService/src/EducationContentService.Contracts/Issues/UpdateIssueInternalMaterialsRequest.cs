namespace EducationContentService.Contracts.Issues;

public sealed record InternalMaterialItem(string ItemType, Guid ReferenceId, bool IsRequired);

public sealed record UpdateIssueInternalMaterialsRequest(IReadOnlyList<InternalMaterialItem> Items);
