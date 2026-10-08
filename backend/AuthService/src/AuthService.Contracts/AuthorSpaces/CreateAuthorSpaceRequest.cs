namespace AuthService.Contracts.AuthorSpaces;

public sealed record CreateAuthorSpaceRequest(Guid UserId, string Slug);
