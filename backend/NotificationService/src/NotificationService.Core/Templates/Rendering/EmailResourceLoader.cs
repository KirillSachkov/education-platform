using System.Collections.Concurrent;
using System.Reflection;

namespace NotificationService.Core.Templates.Rendering;

/// <summary>
/// Загрузчик HTML-шаблонов из embedded resources / Embedded HTML resource loader.
/// Все <c>.html</c> файлы из <c>Core/Templates/Emails/</c> собираются как
/// <c>EmbeddedResource</c> в <c>NotificationService.Core.csproj</c> и читаются раз — при первом
/// обращении, дальше раздаются из кеша.
/// </summary>
internal static class EmailResourceLoader
{
    private const string RESOURCE_PREFIX = "NotificationService.Core.Templates.Emails.";
    private static readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private static readonly Assembly _assembly = typeof(EmailResourceLoader).Assembly;

    /// <summary>
    /// Читает ресурс по имени (относительно папки Emails/, без префикса namespace).
    /// Пример: <c>"_layout.html"</c>, <c>"welcome.html"</c>.
    /// Бросает <see cref="InvalidOperationException"/>, если ресурс не найден — ранний сигнал
    /// о несогласованности catalog.NotificationTemplates и реального набора файлов.
    /// </summary>
    public static string Load(string resourceName)
    {
        return _cache.GetOrAdd(resourceName, static name =>
        {
            string fullName = RESOURCE_PREFIX + name;
            using Stream? stream = _assembly.GetManifestResourceStream(fullName);
            if (stream is null)
                throw new InvalidOperationException(
                    $"Email-ресурс '{fullName}' не найден. Проверь .csproj: <EmbeddedResource Include=\"Templates/Emails/**/*.html\" />.");

            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        });
    }
}
