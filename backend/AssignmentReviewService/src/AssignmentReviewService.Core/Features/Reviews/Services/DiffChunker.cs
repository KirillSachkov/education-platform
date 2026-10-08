using System.Text.RegularExpressions;
using AssignmentReviewService.Core.Vcs.Models;

namespace AssignmentReviewService.Core.Features.Reviews.Services;

/// <summary>
///     Большой-diff chunking (#18): фильтрует non-reviewable файлы (lockfiles,
///     generated / vendored / minified, бинарники) и разбивает оставшиеся
///     source-файлы на последовательные batch'и, каждый в пределах
///     <c>MaxDiffAdditions</c>. RunIteration вызывает AiReviewer per-batch и
///     агрегирует результат. Чистая функция — без I/O, легко юнит-тестить.
/// </summary>
public static partial class DiffChunker
{
    // Точные имена lockfile'ов (case-insensitive по basename).
    private static readonly HashSet<string> LOCKFILE_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "cargo.lock",
        "poetry.lock",
        "composer.lock",
        "gemfile.lock",
        "packages.lock.json",
        "go.sum",
    };

    // Сегменты пути, означающие vendored / generated / build output / IDE-метаданные.
    // `.Contains(segment)` ловит и top-level (".idea/..."), и nested ("a/.idea/...").
    private static readonly string[] VENDORED_SEGMENTS =
    [
        "node_modules/",
        "/dist/",
        "/build/",
        "vendor/",
        "/bin/",
        "/obj/",
        ".next/",
        "__pycache__/",
        // IDE / editor / личные vault'ы — никогда не код задания, но студенты их
        // регулярно коммитят случайно (#679: blackman80#10 — `.idea/` на 95k строк
        // забил весь diff и прошёл как MINOR; IKalentsov#2 — `.obsidian/` дал ложный
        // OFF_TOPIC поверх корректного решения). Чистим до ревью, как build-output.
        ".idea/",
        ".vscode/",
        ".vs/",
        ".obsidian/",
    ];

    // Точные имена editor/OS-мусорных файлов (case-insensitive по basename).
    private static readonly HashSet<string> EDITOR_JUNK_NAMES = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ds_store",
        "thumbs.db",
    };

    /// <summary>
    ///     Результат подготовки diff'а к ревью.
    /// </summary>
    /// <param name="Batches">Последовательные batch'и source-файлов (каждый ≤ лимита).</param>
    /// <param name="FilteredAdditions">Сумма additions по reviewable файлам.</param>
    /// <param name="FilteredFileCount">Кол-во reviewable файлов после фильтрации.</param>
    /// <param name="ExceedsHardCap">true → даже после фильтрации diff превышает hard-cap.</param>
    public sealed record ChunkingResult(
        IReadOnlyList<VcsDiff> Batches,
        int FilteredAdditions,
        int FilteredFileCount,
        bool ExceedsHardCap);

    public static ChunkingResult Prepare(
        VcsDiff diff,
        int maxDiffAdditionsPerBatch,
        int hardMaxAdditions,
        int hardMaxFiles)
    {
        List<VcsDiffFile> reviewable = diff.Files
            .Where(f => !IsNonReviewable(f.Path))
            // Файлы без строковых изменений (0 добавлений и 0 удалений) — показывать
            // модели нечего. Сюда попадают чистые ренеймы/перемещения (только смена пути)
            // и submodule-бампы (status="modified", patch=null) — патча нет. Без фильтра
            // массовый рефактор-перенос (прод-кейс owlillp/DirectoryService#38: 251
            // backend-файл перенесён в backend/) забивает контекст и сбивает модель с
            // реального кода задания. Ренеймы С правками и удаления — реальный diff, остаются.
            // (Бинарники по расширению уже отсеяны IsNonReviewable выше.)
            .Where(f => f.Additions > 0 || f.Deletions > 0)
            .ToList();

        int filteredAdditions = reviewable.Sum(f => f.Additions);
        int filteredFileCount = reviewable.Count;

        bool exceedsHardCap =
            filteredAdditions > hardMaxAdditions || filteredFileCount > hardMaxFiles;
        if (exceedsHardCap)
            return new ChunkingResult([], filteredAdditions, filteredFileCount, ExceedsHardCap: true);

        List<VcsDiff> batches = SplitIntoBatches(diff.HeadSha, reviewable, maxDiffAdditionsPerBatch);
        return new ChunkingResult(batches, filteredAdditions, filteredFileCount, ExceedsHardCap: false);
    }

    private static List<VcsDiff> SplitIntoBatches(
        string headSha,
        List<VcsDiffFile> files,
        int maxAdditionsPerBatch)
    {
        List<VcsDiff> batches = [];
        List<VcsDiffFile> current = [];
        int currentAdditions = 0;

        foreach (VcsDiffFile file in files)
        {
            // Один файл больше лимита — кладём его в собственный batch (не дробим
            // hunks: разрыв patch'а потеряет контекст для LLM, лучше отдать целиком).
            if (file.Additions > maxAdditionsPerBatch)
            {
                if (current.Count > 0)
                {
                    batches.Add(MakeBatch(headSha, current, currentAdditions));
                    current = [];
                    currentAdditions = 0;
                }

                batches.Add(MakeBatch(headSha, [file], file.Additions));
                continue;
            }

            if (currentAdditions + file.Additions > maxAdditionsPerBatch && current.Count > 0)
            {
                batches.Add(MakeBatch(headSha, current, currentAdditions));
                current = [];
                currentAdditions = 0;
            }

            current.Add(file);
            currentAdditions += file.Additions;
        }

        if (current.Count > 0)
            batches.Add(MakeBatch(headSha, current, currentAdditions));

        return batches;
    }

    private static VcsDiff MakeBatch(string headSha, List<VcsDiffFile> files, int additions) =>
        new(headSha, files, additions, files.Sum(f => f.Deletions));

    private static bool IsNonReviewable(string path)
    {
        string normalized = path.Replace('\\', '/');
        string lower = normalized.ToLowerInvariant();
        string basename = normalized[(normalized.LastIndexOf('/') + 1)..];

        if (LOCKFILE_NAMES.Contains(basename))
            return true;

        // OS / editor мусор по точному имени (.DS_Store, Thumbs.db).
        if (EDITOR_JUNK_NAMES.Contains(basename))
            return true;

        if (lower.EndsWith(".lock", StringComparison.Ordinal))
            return true;

        // Vendored / generated / build output по сегменту пути. Префиксный кейс
        // ("node_modules/...") ловим отдельно — он не начинается со слэша.
        if (lower.StartsWith("node_modules/", StringComparison.Ordinal)
            || lower.StartsWith("vendor/", StringComparison.Ordinal)
            || lower.StartsWith("dist/", StringComparison.Ordinal)
            || lower.StartsWith("build/", StringComparison.Ordinal))
            return true;

        foreach (string segment in VENDORED_SEGMENTS)
        {
            if (lower.Contains(segment, StringComparison.Ordinal))
                return true;
        }

        // Generated / minified / designer файлы.
        if (MinifiedOrGeneratedRegex().IsMatch(lower))
            return true;

        // IDE project-файлы по расширению (IntelliJ `.iml`, VS `.user`/`.suo`/`.userprefs`).
        if (IdeProjectFileRegex().IsMatch(lower))
            return true;

        // Бинарники (нет patch'а — GitHub не отдаёт diff для binary). Здесь по
        // расширению; binary без patch'а в любом случае отдаст 0 additions.
        if (BinaryExtensionRegex().IsMatch(lower))
            return true;

        return false;
    }

    [GeneratedRegex(
        @"\.(?:min|generated|designer|g)\.[a-z0-9]+$|\.designer\.cs$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex MinifiedOrGeneratedRegex();

    [GeneratedRegex(
        @"\.(?:iml|user|suo|userprefs)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex IdeProjectFileRegex();

    [GeneratedRegex(
        @"\.(?:png|jpe?g|gif|webp|ico|svg|pdf|zip|gz|tar|woff2?|ttf|eot|mp4|mp3|wav|exe|dll|so|dylib|class|jar|wasm)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex BinaryExtensionRegex();
}
