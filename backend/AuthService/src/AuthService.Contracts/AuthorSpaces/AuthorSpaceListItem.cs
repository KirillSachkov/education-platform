namespace AuthService.Contracts.AuthorSpaces;

public sealed record AuthorSpaceListItem(
    Guid AuthorId,
    string Slug,
    string? DisplayName,
    string? Tagline,
    Guid? AvatarId);
