namespace AccessService.Contracts.InviteLinks.Dtos;

/// <summary>
/// Anonymous landing-page data for an invite link. Exposes only public plan
/// fields — never reveals private invite metadata (usage count, expiry timestamp,
/// creator id, internal label).
/// </summary>
public sealed record InvitePreviewDto(
    string PlanTier,
    string PlanDisplayName,
    string PlanShortDescription,
    Guid? PlanCoverFileId,
    IReadOnlyList<string> PlanFeatures,
    IReadOnlyList<Guid> PlanCourseIds,
    bool IncludesFutureContent,
    bool IsAvailable,
    string? UnavailableReason,
    Guid PlanAuthorId);
