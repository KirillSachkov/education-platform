using System.Globalization;
using System.Text;

using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Core.Subtitles;

public sealed class SrtSubtitleRenderer : ISubtitleRenderer
{
    public string RenderSrt(Transcript transcript)
    {
        if (transcript.Segments.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();

        for (int index = 0; index < transcript.Segments.Count; index++)
        {
            TranscriptSegment segment = transcript.Segments[index];

            if (index > 0)
                builder.AppendLine();

            builder.AppendLine((index + 1).ToString(CultureInfo.InvariantCulture));
            builder.Append(FormatTimestamp(segment.Start));
            builder.Append(" --> ");
            builder.AppendLine(FormatTimestamp(segment.End));
            builder.AppendLine(segment.Text.Trim());
        }

        return builder.ToString();
    }

    private static string FormatTimestamp(TimeSpan value)
    {
        int hours = (int)value.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}");
    }
}
