using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MaterialProcessingService.Domain.Common.ValueObjects;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Infrastructure.Postgres.Configurations;

public sealed class TimecodeGenerationJobConfiguration : IEntityTypeConfiguration<TimecodeGenerationJob>
{
    public const string ACTIVE_JOB_INDEX = "ux_timecode_jobs_video_active";

    public void Configure(EntityTypeBuilder<TimecodeGenerationJob> builder)
    {
        builder.ToTable("timecode_generation_jobs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.VideoAssetId)
            .HasColumnName("video_asset_id")
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
                value => TimecodeEnumValues.ToTimecodeGenerationStatus(value))
            .HasColumnName("status")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Stage)
            .HasConversion(
                stage => stage.ToStorageValue(),
                value => TimecodeEnumValues.ToTimecodeGenerationStage(value))
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

        // Mode ВСЕГДА задаётся в TimecodeGenerationJob.Create — DB-level DEFAULT не нужен.
        // Если поставить HasDefaultValue(TIMECODES), EF Core считает TIMECODES (= default(enum) = 0)
        // «значение не задано» и не отправляет mode в INSERT. Результат: jobs, созданные через
        // GenerateVideoTranscriptHandler с Mode=TRANSCRIPT_ONLY, в БД оказываются с Mode=TIMECODES,
        // и handler НЕ делает early-exit — gases full timecode pipeline вместо остановки на транскрипте.
        // Тот же подвох, что с MaterialKind в EducationContentService (см. backend/CLAUDE.md
        // секцию «Enum Storage Convention»). DB-side DEFAULT остаётся в миграции для backfill
        // существующих строк (legacy = TIMECODES).
        builder.Property(x => x.Mode)
            .HasConversion<string>()
            .HasColumnName("mode")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ModelOverride)
            .HasColumnName("model_override")
            .HasMaxLength(100);

        // TriggerSource ВСЕГДА задаётся в TimecodeGenerationJob.Create (default MANUAL) —
        // НЕ ставим HasDefaultValue (тот же подвох с enum-default, что у Mode выше).
        // DB-side DEFAULT 'MANUAL' живёт в миграции для backfill legacy-строк.
        builder.Property(x => x.TriggerSource)
            .HasConversion<string>()
            .HasColumnName("trigger_source")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.MaterialId)
            .HasColumnName("material_id");

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

        builder.HasIndex(x => new { x.VideoAssetId, x.Status })
            .HasDatabaseName("ix_timecode_jobs_video_status");

        builder.HasIndex(x => new { x.Status, x.UpdatedAt })
            .HasDatabaseName("ix_timecode_jobs_status_updated_at");

        builder.HasIndex(x => x.VideoAssetId)
            .HasFilter("status IN ('QUEUED', 'PROCESSING')")
            .IsUnique()
            .HasDatabaseName(ACTIVE_JOB_INDEX);
    }
}
