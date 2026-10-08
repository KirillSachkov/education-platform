using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.QuestionStudyStates;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class QuestionStudyStateConfiguration : IEntityTypeConfiguration<QuestionStudyState>
{
    public void Configure(EntityTypeBuilder<QuestionStudyState> b)
    {
        b.ToTable("question_study_states");

        b.HasKey(s => s.Id);
        b.Property(s => s.Id).HasColumnName("id");

        b.Property(s => s.UserId).HasColumnName("user_id").IsRequired();
        b.Property(s => s.QuestionId).HasColumnName("question_id").IsRequired();
        b.Property(s => s.TopicId).HasColumnName("topic_id").IsRequired();

        b.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(s => s.LastSeenAt).HasColumnName("last_seen_at").IsRequired();
        b.Property(s => s.NextDueAt).HasColumnName("next_due_at");

        b.Property(s => s.TimesSeen).HasColumnName("times_seen").IsRequired();
        b.Property(s => s.TimesKnown).HasColumnName("times_known").IsRequired();
        b.Property(s => s.TimesWrong).HasColumnName("times_wrong").IsRequired();

        b.Property(s => s.EaseFactor).HasColumnName("ease_factor").IsRequired();
        b.Property(s => s.IntervalDays).HasColumnName("interval_days").IsRequired();
        b.Property(s => s.Repetitions).HasColumnName("repetitions").IsRequired();

        // One study-state row per (user, question).
        b.HasIndex(s => new { s.UserId, s.QuestionId })
            .IsUnique()
            .HasDatabaseName("ux_question_study_states_user_question");

        // SRS due-queue: WHERE user_id=… AND next_due_at <= now ORDER BY next_due_at — covered by
        // (user_id, next_due_at) so the equality + ascending-due scan is one index walk.
        b.HasIndex(s => new { s.UserId, s.NextDueAt })
            .HasDatabaseName("ix_question_study_states_user_next_due");

        // "Мои ошибки" / status filters: WHERE user_id=… AND status=… .
        b.HasIndex(s => new { s.UserId, s.Status })
            .HasDatabaseName("ix_question_study_states_user_status");
    }
}
