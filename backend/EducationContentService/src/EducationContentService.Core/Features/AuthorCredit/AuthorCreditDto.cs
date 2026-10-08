namespace EducationContentService.Core.Features.AuthorCredit;

/// <summary>
///     Author display credit resolved from AuthService — display name plus an optional
///     avatar file id. Used to attribute courses/materials to their author on catalog
///     cards and detail pages (issue #569, model A co-author). The avatar id is resolved
///     to a URL via FileService by the caller (it already batches file lookups); ECS does
///     not round-trip the avatar here.
/// </summary>
public sealed record AuthorCreditDto(
    Guid AuthorId,
    string? DisplayName,
    Guid? AvatarId);
