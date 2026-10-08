using System.Text.Json.Serialization;

namespace AuthService.Contracts;

/// <summary>Минимальный shape ответа GET /user/orgs.</summary>
public sealed record GitHubOrgDto(
    [property: JsonPropertyName("login")] string? Login);
