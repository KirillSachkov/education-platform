namespace ContentAccess;

public sealed record AccessSubject(
    bool IsAuthenticated,
    Guid UserId,
    bool IsAdmin);
