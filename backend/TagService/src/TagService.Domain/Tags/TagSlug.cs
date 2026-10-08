using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TagService.Domain.Tags;

/// <summary>
/// Слаг тега / Tag slug
/// </summary>
public sealed record TagSlug
{
    public const int MIN_LENGTH = 2;
    public const int MAX_LENGTH = 80;

    private static readonly Regex _whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex _invalidChars = new("[^a-z0-9-#+.]+", RegexOptions.Compiled);
    private static readonly Regex _multiDash = new("-+", RegexOptions.Compiled);

    private static readonly Dictionary<char, string> _cyrillicMap = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e",
        ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m",
        ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = string.Empty,
        ['ы'] = "y", ['ь'] = string.Empty, ['э'] = "e", ['ю'] = "yu", ['я'] = "ya"
    };

    private TagSlug(string value) => Value = value;

    public string Value { get; }

    /// <summary>
    /// Создаёт и валидирует слаг из входной строки / Creates and validates a slug from the input string.
    /// </summary>
    /// <param name="value">Исходное значение тега / Source tag value.</param>
    /// <returns>Результат с валидным слагом или ошибкой / Result with a valid slug or an error.</returns>
    public static Result<TagSlug, Error> Of(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GeneralErrors.ValueIsRequired("tag.slug");
        }

        string normalized = Normalize(value);

        if (normalized.Length is > MAX_LENGTH or < MIN_LENGTH)
            return GeneralErrors.ValueIsInvalid("tag.slug");

        return new TagSlug(normalized);
    }

    /// <summary>
    /// Нормализует строку в URL-safe слаг / Normalizes a string into a URL-safe slug.
    /// </summary>
    /// <param name="input">Входная строка / Input string.</param>
    /// <returns>Нормализованный слаг / Normalized slug.</returns>
    private static string Normalize(string input)
    {
        string s = input.Trim().ToLowerInvariant();

        s = TransliterateCyrillicToLatin(s);

        s = RemoveDiacritics(s);

        s = s.Replace('_', '-');
        s = _whitespace.Replace(s, "-");

        s = _invalidChars.Replace(s, "-");

        s = _multiDash.Replace(s, "-").Trim('-');

        if (s.Length > MAX_LENGTH)
            s = s[..MAX_LENGTH].Trim('-');

        return s;
    }

    /// <summary>
    /// Транслитерирует кириллицу в латиницу / Transliterates Cyrillic characters to Latin.
    /// </summary>
    /// <param name="s">Строка для транслитерации / String to transliterate.</param>
    /// <returns>Транслитерированная строка / Transliterated string.</returns>
    private static string TransliterateCyrillicToLatin(string s)
    {
        var sb = new StringBuilder(s.Length);

        foreach (char ch in s)
        {
            if (_cyrillicMap.TryGetValue(ch, out string? repl))
                sb.Append(repl);
            else
                sb.Append(ch);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Удаляет диакритические знаки из текста / Removes diacritical marks from text.
    /// </summary>
    /// <param name="text">Исходный текст / Source text.</param>
    /// <returns>Текст без диакритики / Text without diacritics.</returns>
    private static string RemoveDiacritics(string text)
    {
        string normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);

        foreach (char c in normalized)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);

            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
