using CSharpFunctionalExtensions;
using SharedKernel;

namespace MaterialProcessingService.Core.Media;

public interface IAudioExtractor
{
    Task<Result<IReadOnlyList<AudioChunk>, Error>> ExtractChunksAsync(
        VideoProcessingSource source,
        Guid jobId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Делит уже извлечённый <paramref name="parent"/> mp3-чанк на N равных частей
    ///     через ffmpeg (без перекодирования: <c>-c copy</c>). Используется когда STT
    ///     для этого чанка вернул обрезанный transcript (truncation detection by
    ///     last-segment.End &lt; threshold) — расщепляем на половинки и заново подаём
    ///     каждую в STT.
    /// </summary>
    Task<Result<IReadOnlyList<AudioChunk>, Error>> SplitChunkAsync(
        AudioChunk parent,
        int parts,
        Guid jobId,
        CancellationToken cancellationToken);

    Task CleanupAsync(Guid jobId, CancellationToken cancellationToken);
}
