namespace AuthService.Domain;

/// <summary>Ролевые профили пользователя. Хранится как единая JSONB-колонка <c>role_profiles</c>.</summary>
public sealed record Profiles
{
    /// <summary>Профиль студента; <c>null</c> если роль не назначена.</summary>
    public StudentProfile? Student { get; init; }

    /// <summary>Профиль автора; <c>null</c> если роль не назначена.</summary>
    public AuthorProfile? Author { get; init; }

    /// <summary>Профиль проверяющего; <c>null</c> если роль не назначена.</summary>
    public ReviewerProfile? Reviewer { get; init; }
}
