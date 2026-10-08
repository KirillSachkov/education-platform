using System.Text.Json.Serialization;

namespace AuthService.Contracts;

public sealed record GitHubOrgMembershipDto(
    [property: JsonPropertyName("state")] string? State);
