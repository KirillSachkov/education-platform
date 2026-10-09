using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileService.Core.Features.Videos;

internal static class StoredTranscriptSrt
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public static Result<string, Error> Render(string segmentsJson)
    {
        Segment[] segments;
        try
        {
            segments = JsonSerializer.Deserialize<Segment[]>(segmentsJson, _options) ?? [];
        }
        catch (JsonException)
        {
            return Error.Failure("video.transcript.invalid", "Не удалось прочитать сохранённую расшифровку");
        }

        // Legacy persisted payloads use start/end; current payloads use startSeconds/endSeconds.
        var validSegments = segments
            .Where(segment => segment.StartTime is >= 0 && segment.EndTime > segment.StartTime &&
                              !string.IsNullOrWhiteSpace(segment.Text))
            .OrderBy(segment => segment.StartTime)
            .ToArray();
        if (validSegments.Length == 0)
            return Error.Failure("video.transcript.invalid", "Не удалось прочитать сохранённую расшифровку");

        var builder = new StringBuilder();
        for (int index = 0; index < validSegments.Length; index++)
        {
            Segment segment = validSegments[index];
            if (index > 0)
                builder.AppendLine();
            builder.AppendLine((index + 1).ToString(CultureInfo.InvariantCulture));
            builder.Append(FormatTimestamp(segment.StartTime!.Value));
            builder.Append(" --> ");
            builder.AppendLine(FormatTimestamp(segment.EndTime!.Value));
            builder.AppendLine(segment.Text!.Trim());
        }
        return builder.ToString();
    }

    private static string FormatTimestamp(double seconds)
    {
        TimeSpan value = TimeSpan.FromSeconds(seconds);
        return string.Create(CultureInfo.InvariantCulture,
            $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00},{value.Milliseconds:000}");
    }

    private sealed record Segment(
        double? StartSeconds,
        double? EndSeconds,
        string? Text,
        [property: JsonPropertyName("start")] double? LegacyStartSeconds,
        [property: JsonPropertyName("end")] double? LegacyEndSeconds)
    {
        public double? StartTime => StartSeconds ?? LegacyStartSeconds;
        public double? EndTime => EndSeconds ?? LegacyEndSeconds;
    }
}