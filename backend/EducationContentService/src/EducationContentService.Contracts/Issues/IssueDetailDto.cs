namespace EducationContentService.Contracts.Issues;

public sealed record IssueInternalMaterialDto(
    string ItemType,
    Guid ReferenceId,
    bool IsRequired,
    string? Title,
    string? ImageUrl = null);

public sealed record IssueExternalLinkDto(
    string Url,
    string Title,
    bool IsRequired);

public sealed record IssueDetailDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Content,
    string Status,
    string AccessType,
    bool IsAccessible,
    string SubmissionMode,
    string? SelfCheckInstructions,
    bool RequiresGithubConnection,
    bool RequiresReviewApp,
    bool IsAutoReviewEnabled,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<IssueInternalMaterialDto> InternalMaterials,
    IReadOnlyList<IssueExternalLinkDto> ExternalLinks);
