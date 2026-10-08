namespace AuthService.Contracts;

public record GetMyProfileResponse(
    Guid Id,
    string Name,
    string? DisplayName,
    string Username,
    string Email,
    IReadOnlyList<string> Roles,
    string? Bio,
    ProfilesDto? Profiles,
    bool HasPassword,
    bool HasGitHubLinked,
    bool HasTelegramLinked,
    Guid? AvatarId,
    IReadOnlyList<string> GithubOrgs
);
