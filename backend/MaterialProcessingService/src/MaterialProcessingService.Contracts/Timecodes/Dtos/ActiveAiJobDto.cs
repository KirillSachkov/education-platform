namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

/// <summary>
///     DTO активного AI-job'а (любая из трёх задач). Используется для глобального
///     tracker'а — фронт показывает sticky-bar с прогрессом всех job'ов автора.
/// </summary>
/// <param name="JobId">Unique id job'а в его таблице.</param>
/// <param name="JobKind">«TIMECODES» | «TRANSCRIPT» | «CONTENT» — для UI группировки и иконок.</param>
/// <param name="VideoAssetId">FileService asset id видео — для deeplink на материал.</param>
/// <param name="MaterialId">Если есть привязка (только для CONTENT) — для deeplink.</param>
/// <param name="Status">QUEUED | PROCESSING | COMPLETED | FAILED.</param>
/// <param name="Stage">Текущий стейдж (SOURCE_FETCH | PROBE | AUDIO_EXTRACT | TRANSCRIBE | GENERATE | SAVE).</param>
/// <param name="ProgressPercent">0–100.</param>
/// <param name="ErrorCode">Код ошибки если FAILED.</param>
/// <param name="ErrorMessage">Сообщение для UI если FAILED.</param>
/// <param name="CreatedAt">Когда создан.</param>
public sealed record ActiveAiJobDto(
    Guid JobId,
    string JobKind,
    Guid VideoAssetId,
    Guid? MaterialId,
    string Status,
    string Stage,
    int ProgressPercent,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedAt);

/// <summary>
///     Ответ `GET /material-processing/jobs/active/`. Список активных
///     (QUEUED+PROCESSING) job'ов текущего юзера, объединённый из обеих таблиц
///     (timecode + content). Сортировка по CreatedAt DESC.
/// </summary>
public sealed record GetActiveAiJobsResponse(IReadOnlyList<ActiveAiJobDto> Jobs);
