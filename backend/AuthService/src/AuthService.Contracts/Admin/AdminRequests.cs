namespace AuthService.Contracts.Admin;

public record AdminCreateUserRequest(
    string Email,
    string Username,
    string Password,
    IReadOnlyList<string> Roles);

public record AdminUpdateUserRequest(
    string? Username,
    string? Email,
    bool? EmailConfirmed);

public record AdminSetPasswordRequest(string NewPassword);

public record AdminSetRolesRequest(IReadOnlyList<string> Roles);

public record AdminSetLockoutRequest(bool IsLocked, DateTimeOffset? LockoutEnd);
