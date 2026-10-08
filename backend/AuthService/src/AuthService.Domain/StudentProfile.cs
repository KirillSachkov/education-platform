using AuthService.Domain.ValueObjects;

namespace AuthService.Domain;

/// <summary>Ролевой профиль студента. Хранится как JSONB в <c>user_profiles.student</c>.</summary>
public sealed record StudentProfile
{
    /// <summary>Ссылка на GitHub-профиль (auto-set при OAuth-login, read-only для пользователя).</summary>
    public GitHubUrl? GitHubUrl { get; init; }

    /// <summary>Дата последнего обновления в UTC.</summary>
    public required DateTime UpdatedAt { get; init; }
}
