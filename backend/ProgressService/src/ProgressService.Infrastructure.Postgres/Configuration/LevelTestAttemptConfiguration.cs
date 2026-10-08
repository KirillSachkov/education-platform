using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class LevelTestAttemptConfiguration : IEntityTypeConfiguration<LevelTestAttempt>
{
    public const string ANONYMOUS_UNCLAIMED_INDEX = "ix_level_test_attempts_anonymous_id_unclaimed";

    public void Configure(EntityTypeBuilder<LevelTestAttempt> builder)
    {
        builder.ToTable("level_test_attempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.QuizId)
            .IsRequired()
            .HasColumnName("quiz_id");

        builder.Property(x => x.UserId)
            .HasColumnName("user_id");

        builder.Property(x => x.AnonymousId)
            .HasColumnName("anonymous_id");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.ClaimedAt)
            .HasColumnName("claimed_at");

        builder.Property(x => x.Answers)
            .HasConversion(LevelTestAttemptJson.AnswersConverter, LevelTestAttemptJson.AnswersComparer)
            .HasColumnType("jsonb")
            .HasColumnName("answers")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(x => x.QuestionResults)
            .HasConversion(
                LevelTestAttemptJson.QuestionResultsConverter,
                LevelTestAttemptJson.QuestionResultsComparer)
            .HasColumnType("jsonb")
            .HasColumnName("question_results")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(x => x.SectionScores)
            .HasConversion(
                LevelTestAttemptJson.SectionScoresConverter,
                LevelTestAttemptJson.SectionScoresComparer)
            .HasColumnType("jsonb")
            .HasColumnName("section_scores")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(x => x.GradingConfig)
            .HasConversion(
                LevelTestAttemptJson.GradingConfigConverter,
                LevelTestAttemptJson.GradingConfigComparer)
            .HasColumnType("jsonb")
            .HasColumnName("grading_config")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(x => x.OverallPercent)
            .IsRequired()
            .HasColumnName("overall_percent");

        builder.Property(x => x.Level)
            .HasMaxLength(50)
            .IsRequired()
            .HasColumnName("level");

        // Enum строкой без CHECK и без HasDefaultValue (значение всегда ставит конструктор) —
        // новый статус добавляется без миграции.
        builder.Property(x => x.AiGradingStatus)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired()
            .HasColumnName("ai_grading_status");

        builder.Property(x => x.RecommendedCourseId)
            .HasColumnName("recommended_course_id");

        // Claim-lookup: WHERE anonymous_id = @id AND user_id IS NULL.
        builder.HasIndex(x => x.AnonymousId)
            .HasDatabaseName(ANONYMOUS_UNCLAIMED_INDEX)
            .HasFilter("user_id IS NULL");
    }
}
