using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.Snapshots;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class DailyStatSnapshotConfiguration : IEntityTypeConfiguration<DailyStatSnapshot>
{
    public void Configure(EntityTypeBuilder<DailyStatSnapshot> b)
    {
        b.ToTable("daily_stat_snapshots");

        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");

        b.Property(s => s.SnapshotDate).HasColumnName("snapshot_date").IsRequired();

        b.Property(s => s.SessionsStarted).HasColumnName("sessions_started").IsRequired();
        b.Property(s => s.ActiveUsers).HasColumnName("active_users").IsRequired();
        b.Property(s => s.CompletedSessions).HasColumnName("completed_sessions").IsRequired();
        b.Property(s => s.OpenGrades).HasColumnName("open_grades").IsRequired();
        b.Property(s => s.TotalCostMicroRub).HasColumnName("total_cost_micro_rub").IsRequired();
        b.Property(s => s.AvgAccuracyPct).HasColumnName("avg_accuracy_pct").IsRequired();

        b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        // One row per day — the owner trend reads an ordered range over this unique date.
        b.HasIndex(s => s.SnapshotDate)
            .IsUnique()
            .HasDatabaseName("ux_daily_stat_snapshots_date");
    }
}
