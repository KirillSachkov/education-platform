using System.Globalization;
using System.Text;
using AssignmentReviewService.Core.Features.Reviews.Models;
using AssignmentReviewService.Core.Vcs;
using AssignmentReviewService.Core.Vcs.Models;
using Microsoft.Extensions.Options;

namespace AssignmentReviewService.Core.Features.Reviews.Services;

/// <summary>
///     #798 — собирает <see cref="RepoReviewContext"/> для цикла дозапроса файлов:
///     дерево репозитория (head-коммит PR) + манифесты зависимостей + read-only
///     колбэк выдачи файлов по need_files. Всё best-effort: сбой GitHub на дереве /
///     манифестах НЕ роняет итерацию — ревью просто идёт с меньшим контекстом
///     (несуществующий/недоступный файл возвращается пометкой, не ошибкой).
/// </summary>
public sealed class RepoContextBuilder
{
    /// <summary>Имена/суффиксы манифестов зависимостей, попадающих в карту репо.</summary>
    private static readonly string[] MANIFEST_PATTERNS =
    [
        ".csproj",
        ".fsproj",
        "package.json",
        "Directory.Packages.props",
        "Directory.Build.props",
        "global.json",
        "requirements.txt",
        "go.mod",
    ];

    private readonly IVcsProvider _vcs;
    private readonly IOptions<AssignmentReviewAiOptions> _options;
    private readonly ILogger<RepoContextBuilder> _logger;

    public RepoContextBuilder(
        IVcsProvider vcs,
        IOptions<AssignmentReviewAiOptions> options,
        ILogger<RepoContextBuilder> logger)
    {
        _vcs = vcs;
        _options = options;
        _logger = logger;
    }

    public async Task<RepoReviewContext> BuildAsync(
        string installationId,
        string repoFullName,
        string headSha,
        CancellationToken ct)
    {
        AssignmentReviewRepoContextOptions opts = _options.Value.RepoContext;

        IReadOnlyList<VcsRepoTreeEntry> blobs = [];
        Result<IReadOnlyList<VcsRepoTreeEntry>, Error> treeResult =
            await _vcs.GetRepoTreeAsync(installationId, repoFullName, headSha, ct);
        if (treeResult.IsSuccess)
        {
            blobs = treeResult.Value
                .Where(e => string.Equals(e.Type, "blob", StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Path, StringComparer.Ordinal)
                .ToList();
        }
        else
        {
            // Best-effort: без дерева модель не видит карту, но дозапрос файлов всё
            // равно работает (contents API сам ответит 404 на несуществующий путь).
            _logger.LogWarning(
                "Repo tree fetch failed for {Repo}@{Sha}: {Code} — proceeding without repository map.",
                repoFullName, headSha, treeResult.Error.Messages[0].Code);
        }

        string? treeText = blobs.Count > 0 ? BuildTreeText(blobs, opts.MaxTreeChars) : null;
        IReadOnlyList<FetchedRepoFile> manifests = blobs.Count > 0
            ? await FetchManifestsAsync(installationId, repoFullName, headSha, blobs, opts, ct)
            : [];

        Dictionary<string, VcsRepoTreeEntry>? treeIndex = blobs.Count > 0
            ? blobs.ToDictionary(e => e.Path, StringComparer.Ordinal)
            : null;

        return new RepoReviewContext(
            treeText,
            manifests,
            (paths, fetchCt) => FetchRequestedFilesAsync(
                installationId, repoFullName, headSha, paths, treeIndex, opts, fetchCt));
    }

    /// <summary>
    ///     Дерево как плоский список «путь (размер)», обрезанный по символьному капу.
    ///     Плоский список для LLM надёжнее ASCII-графа: путь можно скопировать в
    ///     need_files байт-в-байт.
    /// </summary>
    private static string BuildTreeText(IReadOnlyList<VcsRepoTreeEntry> blobs, int maxChars)
    {
        StringBuilder sb = new();
        int included = 0;
        foreach (VcsRepoTreeEntry entry in blobs)
        {
            string line = entry.Size is { } size
                ? string.Create(CultureInfo.InvariantCulture, $"{entry.Path} ({size} B)")
                : entry.Path;
            if (sb.Length + line.Length + 1 > maxChars)
                break;
            sb.Append(line).Append('\n');
            included++;
        }

        if (included < blobs.Count)
        {
            sb.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"... (truncated: {blobs.Count - included} more file(s) not shown)"));
        }

        return sb.ToString().TrimEnd();
    }

    private async Task<IReadOnlyList<FetchedRepoFile>> FetchManifestsAsync(
        string installationId,
        string repoFullName,
        string headSha,
        IReadOnlyList<VcsRepoTreeEntry> blobs,
        AssignmentReviewRepoContextOptions opts,
        CancellationToken ct)
    {
        // Корневые манифесты важнее вложенных — сортируем по глубине пути.
        List<VcsRepoTreeEntry> manifestEntries = blobs
            .Where(e => IsManifestPath(e.Path))
            .OrderBy(e => e.Path.Count(c => c == '/'))
            .ThenBy(e => e.Path, StringComparer.Ordinal)
            .Take(Math.Max(0, opts.MaxManifestFiles))
            .ToList();

        List<FetchedRepoFile> result = [];
        foreach (VcsRepoTreeEntry entry in manifestEntries)
        {
            result.Add(await FetchOneAsync(
                installationId, repoFullName, headSha, entry.Path, entry.Size, opts.MaxManifestBytes, ct));
        }

        return result;
    }

    internal static bool IsManifestPath(string path)
    {
        string fileName = path.Contains('/', StringComparison.Ordinal)
            ? path[(path.LastIndexOf('/') + 1)..]
            : path;
        foreach (string pattern in MANIFEST_PATTERNS)
        {
            bool matches = pattern.StartsWith('.')
                ? fileName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)
                : string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
            if (matches)
                return true;
        }

        return false;
    }

    private async Task<IReadOnlyList<FetchedRepoFile>> FetchRequestedFilesAsync(
        string installationId,
        string repoFullName,
        string headSha,
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, VcsRepoTreeEntry>? treeIndex,
        AssignmentReviewRepoContextOptions opts,
        CancellationToken ct)
    {
        List<FetchedRepoFile> result = [];
        foreach (string path in paths)
        {
            // Дерево знаем → отсекам несуществующее/огромное без похода в API.
            if (treeIndex is not null && !treeIndex.ContainsKey(path))
            {
                result.Add(new FetchedRepoFile(path, null, "not found in the repository tree"));
                continue;
            }

            long? knownSize = treeIndex?[path].Size;
            if (knownSize is { } size && size > opts.MaxFileBytes)
            {
                result.Add(new FetchedRepoFile(
                    path, null,
                    string.Create(CultureInfo.InvariantCulture,
                        $"too large ({size} B > cap {opts.MaxFileBytes} B)")));
                continue;
            }

            result.Add(await FetchOneAsync(
                installationId, repoFullName, headSha, path, knownSize, opts.MaxFileBytes, ct));
        }

        return result;
    }

    private async Task<FetchedRepoFile> FetchOneAsync(
        string installationId,
        string repoFullName,
        string headSha,
        string path,
        long? knownSize,
        int maxBytes,
        CancellationToken ct)
    {
        Result<VcsFileContent, Error> contentResult = await _vcs.GetFileContentAsync(
            installationId, repoFullName, path, headSha, ct);
        if (contentResult.IsFailure)
        {
            string code = contentResult.Error.Messages[0].Code;
            _logger.LogWarning(
                "Repo file fetch failed for {Repo}:{Path}@{Sha}: {Code}", repoFullName, path, headSha, code);
            return new FetchedRepoFile(
                path, null,
                string.Equals(code, "vcs.resource.not_found", StringComparison.Ordinal)
                    ? "not found in the repository"
                    : "could not be fetched");
        }

        VcsFileContent content = contentResult.Value;
        if (content.Size > maxBytes)
        {
            return new FetchedRepoFile(
                path, null,
                string.Create(CultureInfo.InvariantCulture,
                    $"too large ({content.Size} B > cap {maxBytes} B)"));
        }

        string? text = DecodeContent(content);
        if (text is null)
            return new FetchedRepoFile(path, null, "binary or unreadable content");

        if (Encoding.UTF8.GetByteCount(text) > maxBytes)
        {
            // Contents API может отдать size меньше фактического (симлинки и т.п.) —
            // страховочный кап по факту декода.
            text = text[..Math.Min(text.Length, maxBytes)];
            return new FetchedRepoFile(path, text, "truncated to the size cap");
        }

        _ = knownSize; // размер из дерева использован выше для pre-flight отсечки
        return new FetchedRepoFile(path, text);
    }

    private static string? DecodeContent(VcsFileContent content)
    {
        try
        {
            string text = string.Equals(content.Encoding, "base64", StringComparison.OrdinalIgnoreCase)
                ? Encoding.UTF8.GetString(Convert.FromBase64String(
                    content.Content.Replace("\n", string.Empty, StringComparison.Ordinal)))
                : content.Content;
            return text.Contains('\0', StringComparison.Ordinal) ? null : text;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
