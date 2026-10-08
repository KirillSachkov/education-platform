using System.Text;
using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal static class TranscriptWindowBuilder
{
    private const int COMPACT_TRANSCRIPT_BLOCK_SECONDS = 45;
    private const int COMPACT_TRANSCRIPT_BLOCK_MAX_CHARS = 500;

    public static List<TranscriptWindow> Build(Transcript transcript, TimeSpan duration, int chunkSeconds)
    {
        int windowSize = Math.Clamp(chunkSeconds / 2, 300, 420);
        int windowStep = Math.Max(windowSize - 60, 180);
        int durationSeconds = (int)Math.Ceiling(duration.TotalSeconds);
        List<TranscriptWindow> windows = [];

        for (int windowStart = 0; windowStart < durationSeconds; windowStart += windowStep)
        {
            int windowEnd = Math.Min(windowStart + windowSize, durationSeconds);

            List<TranscriptSegment> segments = transcript.Segments
                .Where(segment =>
                    segment.End.TotalSeconds > windowStart &&
                    segment.Start.TotalSeconds < windowEnd)
                .ToList();

            if (segments.Count == 0)
                continue;

            windows.Add(new TranscriptWindow(windowStart, windowEnd, segments));
        }

        return windows;
    }

    public static CompactTranscriptBlock[] BuildCompactTranscript(Transcript transcript)
    {
        if (transcript.Segments.Count == 0)
            return [];

        List<CompactTranscriptBlock> blocks = [];
        List<TranscriptSegment> currentSegments = [];
        int currentChars = 0;

        foreach (TranscriptSegment segment in transcript.Segments)
        {
            if (currentSegments.Count == 0)
            {
                currentSegments.Add(segment);
                currentChars = segment.Text.Length;
                continue;
            }

            TranscriptSegment firstSegment = currentSegments[0];
            double durationSeconds = (segment.End - firstSegment.Start).TotalSeconds;
            int nextChars = currentChars + 1 + segment.Text.Length;

            if (durationSeconds > COMPACT_TRANSCRIPT_BLOCK_SECONDS || nextChars > COMPACT_TRANSCRIPT_BLOCK_MAX_CHARS)
            {
                blocks.Add(CreateCompactBlock(currentSegments));
                currentSegments = [segment];
                currentChars = segment.Text.Length;
                continue;
            }

            currentSegments.Add(segment);
            currentChars = nextChars;
        }

        if (currentSegments.Count > 0)
            blocks.Add(CreateCompactBlock(currentSegments));

        return blocks.ToArray();
    }

    private static CompactTranscriptBlock CreateCompactBlock(IReadOnlyList<TranscriptSegment> segments)
    {
        var builder = new StringBuilder();

        foreach (TranscriptSegment segment in segments)
        {
            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append(segment.Text.Trim());
        }

        return new CompactTranscriptBlock(
            segments[0].Start,
            segments[^1].End,
            builder.ToString());
    }
}
