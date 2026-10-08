using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TrainingSessionItemConfiguration : IEntityTypeConfiguration<TrainingSessionItem>
{
    public void Configure(EntityTypeBuilder<TrainingSessionItem> b)
    {
        b.ToTable("training_session_items");

        b.HasKey(i => i.Id);

        // EF generates Id via TimeOrderedGuidValueGenerator on Add (nav-collection child).
        // Domain factory leaves Id=Guid.Empty — signals EF the entity is new (Added state),
        // not detached-existing. See docs/agents/backend-transactions.md rule 4.
        b.Property(i => i.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(i => i.QuestionId).HasColumnName("question_id").IsRequired();

        b.Property(i => i.TopicId).HasColumnName("topic_id").IsRequired();

        b.Property(i => i.QuestionType)
            .HasColumnName("question_type")
            .HasMaxLength(50)
            .IsRequired();

        b.Property(i => i.QuestionText)
            .HasColumnName("question_text")
            .IsRequired();

        b.Property(i => i.OptionsJson)
            .HasColumnName("options_json")
            .HasColumnType("jsonb")
            .IsRequired();

        b.Property(i => i.Section)
            .HasColumnName("section")
            .HasMaxLength(100);

        b.Property(i => i.Difficulty)
            .HasColumnName("difficulty")
            .HasMaxLength(50);

        b.Property(i => i.SortIndex).HasColumnName("sort_index").IsRequired();

        // Server-only grading-key snapshot ({correctOptionIds,referenceAnswer,explanation}).
        // Never surfaced in any response DTO — only the grader reads it.
        b.Property(i => i.GradingKeyJson)
            .HasColumnName("grading_key_json")
            .HasColumnType("jsonb");

        b.Property(i => i.AnswerRaw).HasColumnName("answer_raw");

        b.Property(i => i.ScorePercent).HasColumnName("score_percent");

        b.Property(i => i.Verdict)
            .HasColumnName("verdict")
            .HasConversion<string>()
            .HasMaxLength(50);

        // AI grading feedback for an open answer (#585). Bounded — short interviewer-style note.
        b.Property(i => i.Feedback).HasColumnName("feedback").HasMaxLength(4000);

        b.Property(i => i.AnsweredAt).HasColumnName("answered_at");

        b.HasIndex("session_id", "SortIndex")
            .HasDatabaseName("ix_training_session_items_session_sort");

        // Admin quality/snapshot queries aggregate and join historical attempts by question.
        b.HasIndex(i => i.QuestionId)
            .HasDatabaseName("ix_training_session_items_question_id");
    }
}
