using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.AiUsage;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> b)
    {
        b.ToTable("ai_usage");

        b.HasKey(r => r.Id);
        b.Property(r => r.Id).HasColumnName("id");

        b.Property(r => r.UserId).HasColumnName("user_id").IsRequired();

        b.Property(r => r.Operation)
            .HasColumnName("operation")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(r => r.Model)
            .HasColumnName("model")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(r => r.InputTokens).HasColumnName("input_tokens");
        b.Property(r => r.OutputTokens).HasColumnName("output_tokens");
        b.Property(r => r.TotalTokens).HasColumnName("total_tokens");

        b.Property(r => r.CostMicroRub).HasColumnName("cost_micro_rub").IsRequired();

        b.Property(r => r.SessionId).HasColumnName("session_id");

        b.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        // Per-user accounting reads: WHERE user_id=… [ORDER BY created_at]. Composite covers it.
        b.HasIndex(r => new { r.UserId, r.CreatedAt })
            .HasDatabaseName("ix_ai_usage_user_created");

        // Time-window cost rollups across all users.
        b.HasIndex(r => r.CreatedAt).HasDatabaseName("ix_ai_usage_created");
    }
}
