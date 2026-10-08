namespace AccessService.Contracts.Users;

/// <summary>
/// Lean user projection used by AccessService's `GET /access/users/lookup` endpoint
/// for the «Выдать grant» admin UI. Mirrors <c>AuthService.Contracts.AuthUserLookupDto</c>
/// but keeps the contract local so frontend doesn't reach into AuthService DTOs.
/// </summary>
public sealed record UserLookupResultDto(
    Guid UserId,
    string? DisplayName,
    string? Username,
    string Email,
    Guid? AvatarId);
