namespace AuthService.Domain.AuthorSpaces;

public static class AuthorSpaceErrors
{
    public static Error NotFound(Guid userId) =>
        Error.NotFound("author_space.not_found", $"Пространство автора '{userId}' не найдено");

    public static Error NotFoundBySlug(string slug) =>
        Error.NotFound("author_space.not_found", $"Пространство автора '@{slug}' не найдено");

    public static Error SlugTaken(string slug) =>
        Error.Conflict("author_space.slug_taken", $"Слаг '@{slug}' уже занят");

    public static Error AlreadyExists(Guid userId) =>
        Error.Conflict("author_space.already_exists", "Пространство автора уже создано");
}
