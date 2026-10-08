namespace AuthService.Contracts;

/// <summary>
/// Partial-update request for the current user's account fields.
/// All fields are optional; pass only the ones you want to change. An empty body is a no-op (200 OK).
/// </summary>
public record UpdateMyAccountInfoRequest(string? Username, string? DisplayName);
