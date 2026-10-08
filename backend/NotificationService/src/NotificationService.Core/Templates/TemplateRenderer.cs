using System.Text;

namespace NotificationService.Core.Templates;

/// <summary>
/// Минимальный подстановщик плейсхолдеров <c>{name}</c> / Minimal <c>{name}</c> placeholder substituter.
///
/// Намеренно без условий/циклов/DLR — для текущих ~10 шаблонов этого хватает.
/// Если появится шаблон с условиями — мигрируем на Scriban точечно,
/// меняется только этот класс.
///
/// Отсутствующий ключ не «съедается»: <c>"{missing}"</c> остаётся в выводе как есть.
/// Null-значение подставляется как пустая строка.
/// </summary>
public static class TemplateRenderer
{
    public static string Substitute(string template, TemplateArgs args) =>
        Substitute(template, args, valueTransform: null);

    /// <summary>
    /// Подстановка с опциональным преобразованием значения (HTML-escape, Telegram-escape).
    /// Применяется ТОЛЬКО к значениям args, не к содержимому шаблона. Ключи, помеченные
    /// <see cref="TemplateArgs.WithRaw"/>, transform пропускают (#532) — значение уже
    /// отрендерено сервером с поэлементным экранированием.
    /// </summary>
    public static string Substitute(string template, TemplateArgs args, Func<string, string>? valueTransform)
    {
        if (string.IsNullOrEmpty(template))
            return template;

        int openIndex = template.IndexOf('{', StringComparison.Ordinal);
        if (openIndex < 0)
            return template;

        StringBuilder sb = new(template.Length);
        int cursor = 0;

        while (openIndex >= 0)
        {
            sb.Append(template, cursor, openIndex - cursor);

            int closeIndex = template.IndexOf('}', openIndex + 1);
            if (closeIndex < 0)
            {
                sb.Append(template, openIndex, template.Length - openIndex);
                return sb.ToString();
            }

            string key = template.Substring(openIndex + 1, closeIndex - openIndex - 1);
            if (args.TryGetValue(key, out string? value))
            {
                string rendered = value ?? string.Empty;
                if (valueTransform is not null && !args.IsRaw(key))
                    rendered = valueTransform(rendered);
                sb.Append(rendered);
            }
            else
            {
                sb.Append(template, openIndex, closeIndex - openIndex + 1);
            }

            cursor = closeIndex + 1;
            openIndex = cursor < template.Length
                ? template.IndexOf('{', cursor)
                : -1;
        }

        if (cursor < template.Length)
            sb.Append(template, cursor, template.Length - cursor);

        return sb.ToString();
    }
}
