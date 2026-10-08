namespace MaterialProcessingService.Domain.Timecodes;

/// <summary>
///     Режим работы <see cref="TimecodeGenerationJob"/>:
///     <list type="bullet">
///         <item><c>TIMECODES</c> — полный pipeline: транскрипт → AI тайм-коды → PUT в Kinescope.</item>
///         <item><c>TRANSCRIPT_ONLY</c> — только подготовка транскрипта (SOURCE_FETCH → PROBE →
///             AUDIO_EXTRACT → TRANSCRIBE → SAVE). После завершения автор может отдельно
///             запустить генерацию тайм-кодов и/или конспекта.</item>
///     </list>
/// </summary>
public enum TimecodeGenerationJobMode
{
    TIMECODES,
    TRANSCRIPT_ONLY,
}
