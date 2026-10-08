using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.Snapshots;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TopicMasterySnapshotConfiguration : IEntityTypeConfiguration<TopicMasterySnapshot>
{
    public void Configure(EntityTypeBuilder<TopicMasterySnapshot> b)
    {
        b.ToTable("topic_mastery_snapshots");

        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");

        b.Property(s => s.SnapshotDate).HasColumnName("snapshot_date").IsRequired();
        b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        b.Property(s => s.TopicId).HasColumnName("topic_id").IsRequired();
        b.Property(s => s.Mastery).HasColumnName("mastery").IsRequired();
        b.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        // One mastery point per (day, user, topic). "vs месяц назад" reads a user's rows on two dates.
        b.HasIndex(s => new { s.SnapshotDate, s.UserId, s.TopicId })
            .IsUnique()
            .HasDatabaseName("ux_topic_mastery_snapshots_date_user_topic");

        // The student comparison query filters by user across dates.
        b.HasIndex(s => new { s.UserId, s.SnapshotDate })
            .HasDatabaseName("ix_topic_mastery_snapshots_user_date");
    }
}
