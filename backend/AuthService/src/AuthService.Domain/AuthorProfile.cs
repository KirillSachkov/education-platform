using AuthService.Domain.ValueObjects;

namespace AuthService.Domain;

/// <summary>Ролевой профиль автора. Хранится как JSONB в <c>user_profiles.author</c>.</summary>
public sealed record AuthorProfile
{
    /// <summary>Специализация автора (например, "Backend .NET", "Frontend React").</summary>
    public Specialization? Specialization { get; init; }

    /// <summary>Описание себя как автора курсов.</summary>
    public AboutAsAuthor? AboutAsAuthor { get; init; }

    /// <summary>Дата последнего обновления в UTC.</summary>
    public required DateTime UpdatedAt { get; init; }
}
