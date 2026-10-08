namespace MaterialProcessingService.Core;

public sealed class VideoProcessingOptions
{
    public const string SECTION_NAME = "VideoProcessing";

    public int ChunkSeconds { get; set; } = 900;

    public int MaxVideoDurationMinutes { get; set; } = 180;

    public string DefaultLanguage { get; set; } = "ru";

    /// <summary>
    ///     atempo-фильтр множитель для ускорения аудио перед STT. Биллинг STT-провайдеров
    ///     (Polza/OpenAI) идёт по metadata duration файла → ускорение прямо снижает
    ///     стоимость. 1.0 = без ускорения (исторически), 1.2 = -17% к биллингу.
    ///     Безопасный диапазон [0.5, 2.0] (ffmpeg atempo limit на один filter); вне
    ///     этого диапазона значение клампится. Для русской технической речи
    ///     рекомендуется ≤ 1.25 — выше начинается потеря rare-tokens (термины, аббревиатуры).
    ///     Сегменты транскрипта возвращаются в координатах оригинального видео —
    ///     <see cref="MaterialProcessingService.Core.Transcripts.TranscriptPreparationService"/>
    ///     домножает chunk-local timestamps обратно через <see cref="Media.AudioChunk.Speedup"/>.
    /// </summary>
    public double AudioSpeedup { get; set; } = 1.0;
}
