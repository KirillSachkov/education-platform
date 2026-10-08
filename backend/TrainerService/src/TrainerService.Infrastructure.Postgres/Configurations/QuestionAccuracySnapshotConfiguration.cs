using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.Snapshots;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class QuestionAccuracySnapshotConfiguration : IEntityTypeConfiguration<QuestionAccuracySnapshot>
{
    public void Configure(EntityTypeBuilder<QuestionAccuracySnapshot> b)
    {
        b.ToTable("question_accuracy_snapshots");

        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");

        b.Property(s => s.SnapshotDate).HasColumnName("snapshot_date").IsRequired();
        b.Property(s => s.QuestionId).HasColumnName("question_id").IsRequired();
        b.Property(s => s.Attempts).HasColumnName("attempts").IsRequired();
        b.Property(s => s.Correct).HasColumnName("correct").IsRequired();
        b.Property(s => s.AccuracyPct).HasColumnName("accuracy_pct").IsRequired();
        b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        // One accuracy point per (day, question). Quality-over-time reads a question's ordered date range.
        b.HasIndex(s => new { s.SnapshotDate, s.QuestionId })
            .IsUnique()
            .HasDatabaseName("ux_question_accuracy_snapshots_date_question");

        b.HasIndex(s => new { s.QuestionId, s.SnapshotDate })
            .HasDatabaseName("ix_question_accuracy_snapshots_question_date");
    }
}
