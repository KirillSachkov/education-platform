using AuthService.Contracts;

namespace AuthService.Core.Features.Users.Queries;

/// <summary>Dapper read model для таблицы <c>user_profiles</c>.</summary>
internal sealed record UserProfileRow
{
    public Guid Id { get; init; }
    public string? Bio { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public ProfilesDto? Profiles { get; init; }
    public Guid? AvatarId { get; init; }
}
