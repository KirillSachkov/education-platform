namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
/// Lean Plan projection embedded into <see cref="PlanGrantDto"/> for user-facing
/// «мои планы» views. Avoids a second HTTP round-trip per grant.
/// </summary>
public sealed record PlanSummaryDto(
    Guid Id,
    Guid AuthorId,
    string Slug,
    string DisplayName,
    string? ShortDescription,
    string Tier,
    Guid? CoverFileId,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<Guid> CourseIds,
    bool IncludesFutureContent,
    int? TrialDurationDays,
    bool HasOnboardingEnabled);
