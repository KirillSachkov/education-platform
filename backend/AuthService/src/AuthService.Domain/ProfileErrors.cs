namespace AuthService.Domain;

/// <summary>Доменные ошибки AuthService.</summary>
public static class ProfileErrors
{
    /// <summary>Базовый профиль пользователя не найден.</summary>
    public static Error ProfileNotFound(Guid userId) =>
        Error.NotFound("profile.not_found", $"Профиль пользователя '{userId}' не найден");

    /// <summary>Ролевой профиль не найден.</summary>
    public static Error RoleProfileNotFound(string role) =>
        Error.NotFound("profile.role_not_found", $"Профиль роли '{role}' не найден");

    /// <summary>У пользователя нет нужной роли.</summary>
    public static Error RoleNotAllowed(string role) =>
        Error.Validation("profile.role_not_allowed", $"У пользователя нет роли '{role}'");

    /// <summary>Передана неподдерживаемая роль.</summary>
    public static Error InvalidRole(string role) =>
        Error.Validation("profile.invalid_role",
            $"Роль '{role}' не поддерживается. Допустимые значения: student, author, reviewer");
}
