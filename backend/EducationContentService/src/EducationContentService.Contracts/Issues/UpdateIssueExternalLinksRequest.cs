namespace EducationContentService.Contracts.Issues;

public sealed record ExternalLinkItem(string Url, string Title, bool IsRequired);

public sealed record UpdateIssueExternalLinksRequest(IReadOnlyList<ExternalLinkItem> Items);
