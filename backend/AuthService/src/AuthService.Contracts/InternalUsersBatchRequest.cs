namespace AuthService.Contracts;

public sealed record InternalUsersBatchRequest(IReadOnlyList<Guid> UserIds);
