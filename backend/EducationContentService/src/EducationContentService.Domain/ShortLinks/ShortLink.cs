using System.Security.Cryptography;

namespace EducationContentService.Domain.ShortLinks;

/// <summary>
///     Aggregate Root — короткая share-ссылка материала (youtu.be-style).
///     Ровно один код на материал (unique-индекс по <see cref="MaterialId"/>,
///     get-or-create идемпотентен). Код — криптослучайный base62, иммутабелен.
///     Публичный URL: <c>/s/{code}</c> → 302 → <c>/knowledge-base/{materialId}</c>.
/// </summary>
public sealed class ShortLink
{
    public const int CODE_LENGTH = 8;

    private const string BASE62_ALPHABET =
        "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    private ShortLink(Guid materialId, string code)
    {
        Id = Guid.CreateVersion7();
        MaterialId = materialId;
        Code = code;
        CreatedAt = DateTime.UtcNow;
    }

    // EF Core
    private ShortLink()
    {
    }

    public Guid Id { get; }

    /// <summary>Уникальный URL-safe base62 код (см. <see cref="CODE_LENGTH"/>).</summary>
    public string Code { get; } = null!;

    public Guid MaterialId { get; }

    public DateTime CreatedAt { get; }

    public static ShortLink Create(Guid materialId) => new(materialId, GenerateCode());

    private static string GenerateCode() =>
        RandomNumberGenerator.GetString(BASE62_ALPHABET, CODE_LENGTH);
}
