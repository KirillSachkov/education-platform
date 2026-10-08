using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Transcripts;
using MaterialProcessingService.Domain.Transcripts.ValueObjects;

namespace MaterialProcessingService.Infrastructure.Postgres.Configurations;

public sealed class VideoTranscriptConfiguration : IEntityTypeConfiguration<VideoTranscript>
{
    private static readonly JsonSerializerOptions _segmentsJsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<VideoTranscript> builder)
    {
        builder.ToTable("video_transcripts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.VideoAssetId)
            .HasColumnName("video_asset_id")
            .IsRequired();

        builder.Property(x => x.AssetVersion)
            .HasColumnName("asset_version")
            .IsRequired();

        builder.Property(x => x.Duration)
            .HasConversion(
                value => value.Seconds,
                value => TranscriptDuration.Create(TimeSpan.FromSeconds(value)).Value)
            .HasColumnName("duration_seconds")
            .IsRequired();

        builder.Property(x => x.Language)
            .HasConversion(
                value => value.Value,
                value => Language.Create(value).Value)
            .HasColumnName("language")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.Segments)
            .HasConversion(
                value => SerializeSegments(value),
                value => DeserializeSegments(value))
            .HasColumnName("segments_json")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.HasIndex(x => new { x.VideoAssetId, x.AssetVersion })
            .IsUnique()
            .HasDatabaseName("ix_video_transcripts_video_asset_version");
    }

    private static string SerializeSegments(TranscriptSegments segments)
    {
        TranscriptSegmentStorageModel[] payload = segments.Items
            .Select(segment => new TranscriptSegmentStorageModel
            {
                StartSeconds = segment.Range.StartSeconds,
                EndSeconds = segment.Range.EndSeconds,
                Text = segment.Text.Value,
            })
            .ToArray();

        return JsonSerializer.Serialize(payload, _segmentsJsonOptions);
    }

    private static TranscriptSegments DeserializeSegments(string json)
    {
        TranscriptSegmentStorageModel[] payload =
            JsonSerializer.Deserialize<TranscriptSegmentStorageModel[]>(json, _segmentsJsonOptions) ?? [];
        List<VideoTranscriptSegment> segments = [];

        foreach (TranscriptSegmentStorageModel item in payload)
        {
            double? startSeconds = item.StartSeconds ?? item.LegacyStartSeconds;
            double? endSeconds = item.EndSeconds ?? item.LegacyEndSeconds;

            if (startSeconds is null || endSeconds is null)
                continue;

            var rangeResult = TranscriptSegmentRange.Create(startSeconds.Value, endSeconds.Value);
            if (rangeResult.IsFailure)
                continue;

            var textResult = TranscriptSegmentText.Create(item.Text ?? string.Empty);
            if (textResult.IsFailure)
                continue;

            var segmentResult = VideoTranscriptSegment.Create(rangeResult.Value, textResult.Value);
            if (segmentResult.IsFailure)
                continue;

            segments.Add(segmentResult.Value);
        }

        var segmentsResult = TranscriptSegments.Create(segments);
        if (segmentsResult.IsFailure)
            throw new JsonException("Persisted transcript does not contain valid segments.");

        return segmentsResult.Value;
    }

    private sealed class TranscriptSegmentStorageModel
    {
        [JsonPropertyName("startSeconds")]
        public double? StartSeconds { get; init; }

        [JsonPropertyName("endSeconds")]
        public double? EndSeconds { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("start")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LegacyStartSeconds { get; init; }

        [JsonPropertyName("end")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LegacyEndSeconds { get; init; }
    }
}
