namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Admin/moderator approval payload for catalog visibility (issue #569).
///     <c>true</c> — listed in the public catalog; <c>false</c> — hidden pending moderation.
/// </summary>
public sealed record SetCatalogListingRequest(bool Listed);
