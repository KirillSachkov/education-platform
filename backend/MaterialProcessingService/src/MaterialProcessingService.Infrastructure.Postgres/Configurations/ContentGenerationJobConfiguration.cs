using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.ContentDrafts;

namespace MaterialProcessingService.Infrastructure.Postgres.Configurations;

public sealed class ContentGenerationJobConfiguration : IEntityTypeConfiguration<ContentGenerationJob>
{
    public const string ACTIVE_JOB_INDEX = "ux_content_jobs_video_material_active";

    public void Configure(EntityTypeBuilder<ContentGenerationJob> builder)
    {
        builder.ToTable("content_generation_jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.VideoAssetId)
            .HasColumnName("video_asset_id")
            .IsRequired();

        builder.Property(x => x.MaterialId)
            .HasColumnName("material_id")
            .IsRequired();

        builder.Property(x => x.AssetVersion)
            .HasColumnName("asset_version")
            .IsRequired();

        builder.Property(x => x.RequestedByUserId)
            .HasColumnName("requested_by_user_id")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion(
                status => status.ToStorageValue(),
                value => ContentGenerationEnumValues.ToContentGenerationStatus(value))
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Stage)
            .HasConversion(
                stage => stage.ToStorageValue(),
                value => ContentGenerationEnumValues.ToContentGenerationStage(value))
            .HasColumnName("stage")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ProgressPercent)
            .HasColumnName("progress_percent")
            .IsRequired();

        builder.Property(x => x.SourceType)
            .HasConversion(
                value => value.Value,
                value => ProcessingSourceType.Create(value).Value)
            .HasColumnName("source_type")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ModelOverride)
            .HasColumnName("model_override")
            .HasMaxLength(100);

        builder.Property(x => x.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(128);

        builder.Property(x => x.ErrorMessage)
            .HasColumnName("error_message")
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .HasColumnName("started_at");

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.HasIndex(x => new { x.VideoAssetId, x.MaterialId, x.Status })
            .HasDatabaseName("ix_content_jobs_video_material_status");

        builder.HasIndex(x => new { x.Status, x.UpdatedAt })
            .HasDatabaseName("ix_content_jobs_status_updated_at");

        builder.HasIndex(x => new { x.VideoAssetId, x.MaterialId })
            .HasFilter("status IN ('QUEUED', 'PROCESSING')")
            .IsUnique()
            .HasDatabaseName(ACTIVE_JOB_INDEX);
    }
}
