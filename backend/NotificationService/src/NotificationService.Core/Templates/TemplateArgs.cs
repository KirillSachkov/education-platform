using System.Collections.Frozen;

namespace NotificationService.Core.Templates;

/// <summary>
/// Обёртка над словарём аргументов шаблона / Wrapper over template arguments dictionary.
/// Immutable, case-sensitive (<see cref="StringComparer.Ordinal"/>), можно создать через
/// <see cref="Of(ValueTuple{string, string}[])"/> или <see cref="Empty"/>.
///
/// Используется как входной параметр <see cref="Rendering.IChannelRenderer"/> — renderer подставляет
/// значения в плейсхолдеры <c>{key}</c> шаблона. Null-values допустимы и подставляются как "".
///
/// <para>
/// <b>Raw-аргументы (#532):</b> ключ, добавленный через <see cref="WithRaw"/>, при подстановке
/// НЕ прогоняется через channel-escape (HTML-encode / MarkdownV1-escape) — значение считается
/// уже-отрендеренной разметкой. Контракт: handler обязан собрать raw-значение целиком на
/// сервере, поэлементно экранируя любые user-derived подстроки (заголовки и т.п.). Никогда
/// не класть в raw сырой user input.
/// </para>
/// </summary>
public sealed class TemplateArgs
{
    public static readonly TemplateArgs Empty = new(FrozenDictionary<string, string?>.Empty);

    private static readonly FrozenSet<string> NO_RAW_KEYS =
        new HashSet<string>(StringComparer.Ordinal).ToFrozenSet(StringComparer.Ordinal);

    private readonly IReadOnlyDictionary<string, string?> _inner;
    private readonly FrozenSet<string> _rawKeys;

    public TemplateArgs(IReadOnlyDictionary<string, string?> inner)
        : this(inner, NO_RAW_KEYS)
    {
    }

    private TemplateArgs(IReadOnlyDictionary<string, string?> inner, FrozenSet<string> rawKeys)
    {
        _inner = inner;
        _rawKeys = rawKeys;
    }

    /// <summary>
    /// Удобный builder из набора пар / Convenient builder from key/value pairs.
    /// </summary>
    public static TemplateArgs Of(params (string Key, string? Value)[] pairs)
    {
        if (pairs.Length == 0)
            return Empty;

        Dictionary<string, string?> dict = new(pairs.Length, StringComparer.Ordinal);
        foreach ((string key, string? value) in pairs)
            dict[key] = value;

        return new TemplateArgs(dict.ToFrozenDictionary(StringComparer.Ordinal));
    }

    public int Count => _inner.Count;

    public bool TryGetValue(string key, out string? value) => _inner.TryGetValue(key, out value);

    /// <summary>Ключ помечен как raw — channel-escape при подстановке пропускается.</summary>
    public bool IsRaw(string key) => _rawKeys.Contains(key);

    /// <summary>
    /// Новая копия с добавленным/перезаписанным ключом. Immutable-friendly — используется
    /// dispatcher'ом для inject'а <c>{openUrl}</c> после создания notification.
    /// </summary>
    public TemplateArgs With(string key, string? value)
    {
        Dictionary<string, string?> dict = new(_inner.Count + 1, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> kvp in _inner)
            dict[kvp.Key] = kvp.Value;
        dict[key] = value;
        return new TemplateArgs(dict.ToFrozenDictionary(StringComparer.Ordinal), _rawKeys);
    }

    /// <summary>
    /// Как <see cref="With"/>, но значение помечается raw: подставляется БЕЗ channel-escape.
    /// Значение обязано быть собрано сервером с поэлементным экранированием user-derived частей.
    /// </summary>
    public TemplateArgs WithRaw(string key, string? value)
    {
        Dictionary<string, string?> dict = new(_inner.Count + 1, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> kvp in _inner)
            dict[kvp.Key] = kvp.Value;
        dict[key] = value;

        HashSet<string> raw = new(_rawKeys, StringComparer.Ordinal) { key };
        return new TemplateArgs(
            dict.ToFrozenDictionary(StringComparer.Ordinal),
            raw.ToFrozenSet(StringComparer.Ordinal));
    }
}
