namespace AuthService.Contracts;

public sealed record UserIdsByGithubOrgResponse(
    string OrgSlug,
    IReadOnlyList<Guid> UserIds);
