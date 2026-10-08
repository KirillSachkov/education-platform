using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MaterialProcessingService.Domain.AiSettings;

namespace MaterialProcessingService.Infrastructure.Postgres.Configurations;

public sealed class AiModelSettingsConfiguration : IEntityTypeConfiguration<AiModelSettings>
{
    public void Configure(EntityTypeBuilder<AiModelSettings> builder)
    {
        builder.ToTable("ai_model_settings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        ConfigureSlot(builder, x => x.SpeechToText!, "stt");
        ConfigureSlot(builder, x => x.TimecodeGeneration!, "timecode");
        ConfigureSlot(builder, x => x.ContentGeneration!, "content");

        // Домен всегда задаёт значение — без HasDefaultValue (тот же enum/default-подвох
        // что у Mode/TriggerSource). DB-side DEFAULT true живёт в миграции для backfill
        // существующей singleton-строки. Issue #648.
        builder.Property(x => x.AutoProcessVideosEnabled)
            .HasColumnName("auto_process_videos_enabled")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.UpdatedByUserId)
            .HasColumnName("updated_by_user_id");
    }

    private static void ConfigureSlot(
        EntityTypeBuilder<AiModelSettings> builder,
        System.Linq.Expressions.Expression<Func<AiModelSettings, AiModelSlot?>> selector,
        string columnPrefix)
    {
        builder.OwnsOne(selector, slot =>
        {
            slot.Property(s => s.Model)
                .HasColumnName($"{columnPrefix}_model")
                .HasMaxLength(AiModelSlot.MAX_MODEL_LENGTH)
                .IsRequired();

            slot.Property(s => s.Temperature)
                .HasColumnName($"{columnPrefix}_temperature");

            slot.Property(s => s.MaxOutputTokens)
                .HasColumnName($"{columnPrefix}_max_output_tokens");

            slot.Property(s => s.TimeoutSeconds)
                .HasColumnName($"{columnPrefix}_timeout_seconds");
        });

        builder.Navigation(selector).IsRequired();
    }
}
