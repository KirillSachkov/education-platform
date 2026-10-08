using AuthService.Domain.ValueObjects;

namespace AuthService.Domain;

/// <summary>Ролевой профиль проверяющего. Хранится как JSONB в <c>user_profiles.reviewer</c>.</summary>
public sealed record ReviewerProfile
{
    /// <summary>Максимальное количество задач на одновременную проверку.</summary>
    public int? ReviewCapacity { get; init; }

    /// <summary>Область экспертизы (например, "Алгоритмы", "System Design").</summary>
    public Expertise? Expertise { get; init; }

    /// <summary>Дата последнего обновления в UTC.</summary>
    public required DateTime UpdatedAt { get; init; }
}
