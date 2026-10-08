using System.Security.Cryptography;

namespace AccessService.Domain;

/// <summary>
/// Value object: 22-символьный base62 токен инвайт-ссылки /
/// Value object: 22-character base62 token used in invite links.
/// </summary>
public sealed class InviteToken : ValueObject
{
    private const string ALPHABET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    public const int LENGTH = 22;

    private InviteToken(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Генерирует токен с помощью криптостойкого RNG /
    /// Generates a token using a crypto-strength RNG.
    /// </summary>
    /// <remarks>
    /// Использует <see cref="RandomNumberGenerator.GetInt32(int)"/> — внутри rejection
    /// sampling, гарантирует uniform-распределение по <c>ALPHABET</c>. Прямой
    /// <c>bytes[i] % ALPHABET.Length</c> над 256-байтным универсумом давал смещение
    /// (256 = 4×62 + 8 → первые 8 символов 25% over-represented), что не критично на
    /// токенах с ~131 битом энтропии, но контракт VO "crypto-strength" требует
    /// несмещённой выборки. Issue #252.
    /// </remarks>
    public static InviteToken Generate()
    {
        Span<char> chars = stackalloc char[LENGTH];
        for (int i = 0; i < LENGTH; i++)
        {
            chars[i] = ALPHABET[RandomNumberGenerator.GetInt32(ALPHABET.Length)];
        }

        return new InviteToken(new string(chars));
    }

    /// <summary>
    /// Парсит существующий токен (например, из URL) /
    /// Parses an existing token (e.g. from a URL).
    /// </summary>
    public static Result<InviteToken, Error> Of(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length != LENGTH)
        {
            return Error.Validation("invite.token.invalid", "Некорректный формат токена");
        }

        foreach (char c in raw)
        {
            if (!ALPHABET.Contains(c, StringComparison.Ordinal))
            {
                return Error.Validation("invite.token.invalid", "Некорректный формат токена");
            }
        }

        return new InviteToken(raw);
    }

    protected override IEnumerable<IComparable> GetEqualityComponents()
    {
        yield return Value;
    }
}
