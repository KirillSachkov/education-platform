namespace EducationContentService.Contracts.Courses;

/// <summary>
///     A PUBLISHED course awaiting catalog-listing approval (issue #569). Surfaced to
///     admins/moderators in the moderation queue, enriched with the author's display name
///     (and avatar URL, best-effort) so the reviewer knows whose course they're approving.
/// </summary>
public sealed record PendingCatalogCourseDto(
    Guid Id,
    Guid AuthorId,
    string Slug,
    string Title,
    string Description,
    string Kind,
    Guid? ImageId,
    DateTime CreatedAt,
    string? AuthorDisplayName,
    string? AuthorAvatarUrl);
