namespace AuthService.Contracts;

public sealed record PublicProfileResponse(
    Guid Id,
    string? DisplayName,
    string? Username,
    string? Bio,
    Guid? AvatarId,
    string? Specialization,
    string? AboutAsAuthor,
    string? GitHubUrl);
