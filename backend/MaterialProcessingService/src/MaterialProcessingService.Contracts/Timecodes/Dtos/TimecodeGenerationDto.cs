namespace MaterialProcessingService.Contracts.Timecodes.Dtos;

public sealed record TimecodeGenerationDto(
    Guid JobId,
    string Status,
    string Stage,
    int ProgressPercent,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime RequestedAt,
    /// <summary>
    ///     Режим job'а: "TIMECODES" — full pipeline (transcript → AI timecodes →
    ///     Kinescope chapters); "TRANSCRIPT_ONLY" — только подготовка транскрипта,
    ///     handler делает early-exit после стадии Save транскрипта.
    ///     Frontend читает чтобы показать разный UI: для TRANSCRIPT_ONLY кнопка
    ///     «Сгенерировать тайм-коды» становится доступной по завершении job'а
    ///     (а для TIMECODES уже всё сделано — главы в Kinescope).
    /// </summary>
    string Mode = "TIMECODES");
