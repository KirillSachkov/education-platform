namespace MaterialProcessingService.Core.Media;

/// <summary>
///     Один mp3-фрагмент аудиодорожки.
///
///     <para>
///     <see cref="Offset"/> и <see cref="Duration"/> — координаты в ОРИГИНАЛЬНОМ
///     видео-времени (до atempo-ускорения). Физическая длительность файла на диске
///     равна <c>Duration.TotalSeconds / Speedup</c>: при <see cref="Speedup"/>=1.2
///     chunk покрывает 900s оригинала, но mp3-файл длится 750s.
///     </para>
///
///     <para>
///     STT возвращает сегменты в chunk-local time (от 0 до physical duration). Маппинг
///     в global original time делает <see cref="Transcripts.TranscriptPreparationService"/>
///     через <c>segment.start * Speedup + Offset</c>.
///     </para>
/// </summary>
public sealed record AudioChunk(
    string Path,
    TimeSpan Offset,
    TimeSpan Duration,
    double Speedup = 1.0);
