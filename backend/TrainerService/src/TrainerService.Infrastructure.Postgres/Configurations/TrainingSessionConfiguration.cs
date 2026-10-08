using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TrainingSessionConfiguration : IEntityTypeConfiguration<TrainingSession>
{
    public void Configure(EntityTypeBuilder<TrainingSession> b)
    {
        b.ToTable("training_sessions");

        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");

        b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        b.Property(s => s.Version)
            .HasColumnName("version")
            .IsRequired()
            .IsConcurrencyToken();

        b.Property(s => s.Mode)
            .HasColumnName("mode")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(s => s.TrackId).HasColumnName("track_id").IsRequired(false);

        ValueComparer<IReadOnlyList<Guid>> guidListComparer = new(
            (a, c) => (a == null && c == null) || (a != null && c != null && a.SequenceEqual(c)),
            v => v.Aggregate(0, (acc, g) => HashCode.Combine(acc, g.GetHashCode())),
            v => (IReadOnlyList<Guid>)v.ToList());

        b.Property(s => s.TopicIds)
            .HasColumnName("topic_ids")
            .HasColumnType("uuid[]")
            .IsRequired()
            .HasConversion(
                v => v.ToArray(),
                v => (IReadOnlyList<Guid>)v.ToList())
            .Metadata.SetValueComparer(guidListComparer);

        b.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(s => s.TimeLimitSeconds).HasColumnName("time_limit_seconds");

        b.Property(s => s.RevealPolicy)
            .HasColumnName("reveal_policy")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(s => s.StartedAt).HasColumnName("started_at").IsRequired();
        b.Property(s => s.CompletedAt).HasColumnName("completed_at");
        b.Property(s => s.ScorePercent).HasColumnName("score_percent");

        // AI grading of open answers (#585). NOT_REQUIRED for auto-graded sessions; the
        // background grader walks PENDING → GRADING → GRADED for mock-interview open answers.
        b.Property(s => s.GradingStatus)
            .HasColumnName("grading_status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        b.Property(s => s.AiOverallFeedback)
            .HasColumnName("ai_overall_feedback")
            .HasMaxLength(4000);

        b.Property(s => s.AiWeakTopicsJson)
            .HasColumnName("ai_weak_topics_json")
            .HasMaxLength(2000);

        b.Property(s => s.AiStrengthsJson)
            .HasColumnName("ai_strengths_json")
            .HasMaxLength(2000);

        // Child collection — items live in a separate table (training_session_items).
        b.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey("session_id")
            .OnDelete(DeleteBehavior.Cascade);

        // Field-access: Items is exposed via `Items => _items` getter. Without Field-mode
        // EF reads the navigation as a read-only property and a new item added to private
        // `_items` is tracked as Modified, not Added → UPDATE on 0 rows → concurrency error.
        b.Navigation(s => s.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasField("_items");

        b.HasIndex(s => s.UserId).HasDatabaseName("ix_training_sessions_user_id");

        // History query: WHERE user_id=… [AND mode=…] [AND track_id=…] ORDER BY started_at DESC LIMIT n.
        // Composite (user_id, started_at) lets PG satisfy the equality + descending-sorted top-N in one
        // index scan; the optional mode/track_id predicates filter the already-ordered rows (#568).
        b.HasIndex(s => new { s.UserId, s.StartedAt })
            .HasDatabaseName("ix_training_sessions_user_id_started_at");

        // Cross-user admin analytics and daily snapshots filter by these timestamps. The user-leading
        // history index above cannot serve a range scan that has no user predicate.
        b.HasIndex(s => s.StartedAt)
            .HasDatabaseName("ix_training_sessions_started_at");
        b.HasIndex(s => s.CompletedAt)
            .HasDatabaseName("ix_training_sessions_completed_at");

        // Recovery sweep for durable-by-database mock grading (PENDING/GRADING).
        b.HasIndex(s => s.GradingStatus)
            .HasDatabaseName("ix_training_sessions_grading_status");
    }
}
