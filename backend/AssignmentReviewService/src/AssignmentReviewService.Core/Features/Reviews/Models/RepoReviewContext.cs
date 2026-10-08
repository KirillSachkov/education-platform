namespace AssignmentReviewService.Core.Features.Reviews.Models;

/// <summary>
///     Файл репозитория, подготовленный для промпта (#798). <see cref="Content"/> —
///     текст файла (уже обрезанный по капам) или <c>null</c>, когда файл передать
///     нельзя; тогда <see cref="Note"/> объясняет причину («не найден», «слишком
///     большой», «ошибка чтения») — модель видит пометку и не просит файл повторно.
/// </summary>
public sealed record FetchedRepoFile(string Path, string? Content, string? Note = null);

/// <summary>
///     Контекст репозитория для цикла дозапроса файлов (#798). Собирается
///     <c>RepoContextBuilder</c>'ом в RunIteration и передаётся в <c>AiReviewer</c>:
///     <see cref="TreeText"/> + <see cref="Manifests"/> идут в промпт с первого же
///     вызова, <see cref="FetchFilesAsync"/> — read-only колбэк, которым цикл
///     достаёт файлы, запрошенные моделью через <c>need_files</c> (head-коммит PR,
///     капы по размеру/количеству применяет сам колбэк).
/// </summary>
public sealed record RepoReviewContext(
    string? TreeText,
    IReadOnlyList<FetchedRepoFile> Manifests,
    Func<IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<FetchedRepoFile>>> FetchFilesAsync);

/// <summary>
///     Данные repo-контекста для конкретного LLM-вызова (#798) — то, что
///     <c>PromptBuilder</c> рендерит в блоки <c>#0e</c>/<c>#0f</c>.
/// </summary>
public sealed record RepoContextPromptData(
    string? TreeText,
    IReadOnlyList<FetchedRepoFile> Manifests,
    IReadOnlyList<FetchedRepoFile> FetchedFiles,
    bool AllowFileRequests,
    int MaxFilesPerRound,
    int RemainingFileBudget,
    bool BudgetExhausted);
