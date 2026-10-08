using System.Text.RegularExpressions;
using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Infrastructure.AI.Transcription;

internal static class SpeechTranscriptNormalizer
{
    public static SpeechTranscriptNormalizationResult NormalizeOrEstimate(
        IReadOnlyList<SpeechToTextSegmentResponse> segments,
        TimeSpan duration)
    {
        TranscriptSegment[] normalizedSegments = NormalizeSegments(segments, duration);
        if (normalizedSegments.Length > 0)
            return new SpeechTranscriptNormalizationResult(normalizedSegments, SpeechTimestampSource.Model);

        string joinedText = string.Join(" ", segments.Select(static segment => segment.Text));
        return new SpeechTranscriptNormalizationResult(
            BuildEstimatedSegments(joinedText, duration),
            SpeechTimestampSource.Estimated);
    }

    /// <summary>
    ///     Используется когда STT вернул full text без segments[] (gpt-4o-transcribe
    ///     не поддерживает verbose_json — игнорирует timestamp_granularities). Дробит
    ///     текст по предложениям, распределяет время пропорционально длине предложений.
    /// </summary>
    public static SpeechTranscriptNormalizationResult EstimateFromText(string text, TimeSpan duration) =>
        new(BuildEstimatedSegments(text, duration), SpeechTimestampSource.Estimated);

    private static TranscriptSegment[] NormalizeSegments(
        IReadOnlyList<SpeechToTextSegmentResponse> segments,
        TimeSpan duration)
    {
        return segments
            .Where(static segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(segment =>
            {
                double startSeconds = Math.Clamp(segment.StartSeconds, 0, duration.TotalSeconds);
                double endSeconds = Math.Clamp(segment.EndSeconds, startSeconds, duration.TotalSeconds);

                return new TranscriptSegment(
                    TimeSpan.FromSeconds(startSeconds),
                    TimeSpan.FromSeconds(endSeconds),
                    segment.Text.Trim());
            })
            .Where(static segment => segment.End > segment.Start)
            .OrderBy(static segment => segment.Start)
            .ToArray();
    }

    private static TranscriptSegment[] BuildEstimatedSegments(string text, TimeSpan duration)
    {
        string normalizedText = text
            .Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalizedText))
            return [];

        string[] sentences = Regex
            .Split(normalizedText, @"(?<=[\.\!\?…])\s+|\r?\n+")
            .Select(static sentence => sentence.Trim())
            .Where(static sentence => !string.IsNullOrWhiteSpace(sentence))
            .ToArray();

        if (sentences.Length == 0)
            return [new TranscriptSegment(TimeSpan.Zero, duration, normalizedText)];

        int totalChars = Math.Max(1, sentences.Sum(static sentence => sentence.Length));
        double totalSeconds = Math.Max(1, duration.TotalSeconds);
        double consumedSeconds = 0;
        var result = new List<TranscriptSegment>(sentences.Length);

        for (int index = 0; index < sentences.Length; index++)
        {
            string sentence = sentences[index];
            // Если предыдущие предложения полностью съели duration (consumedSeconds
            // близко к totalSeconds), оставшимся даём минимум 1 секунду — иначе
            // start==end и downstream TranscriptSegmentRange.Create отвергает range.
            // Допускаем что последний segment может выйти за totalSeconds — это
            // лучше чем потерять часть текста.
            double remaining = totalSeconds - consumedSeconds;
            double segmentSeconds = index == sentences.Length - 1
                ? Math.Max(1, remaining)
                : Math.Max(1, Math.Round(totalSeconds * sentence.Length / totalChars));

            TimeSpan start = TimeSpan.FromSeconds(consumedSeconds);
            TimeSpan end = TimeSpan.FromSeconds(consumedSeconds + segmentSeconds);

            result.Add(new TranscriptSegment(start, end, sentence));
            consumedSeconds = end.TotalSeconds;
        }

        return result.ToArray();
    }
}

internal sealed record SpeechTranscriptNormalizationResult(
    TranscriptSegment[] Segments,
    SpeechTimestampSource TimestampSource);
