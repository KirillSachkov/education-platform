namespace AuthService.Contracts.AuthorSpaces;

public sealed record AuthorSpaceRouteResponse(
    Guid AuthorId,
    string Slug);
