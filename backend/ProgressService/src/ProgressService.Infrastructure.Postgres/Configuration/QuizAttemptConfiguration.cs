using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class QuizAttemptConfiguration : IEntityTypeConfiguration<QuizAttempt>
{
    public const string USER_QUIZ_INDEX = "ix_quiz_attempts_user_id_quiz_id";

    public void Configure(EntityTypeBuilder<QuizAttempt> builder)
    {
        builder.ToTable("quiz_attempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.QuizId)
            .IsRequired()
            .HasColumnName("quiz_id");

        builder.Property(x => x.MaterialId)
            .HasColumnName("material_id");

        builder.Property(x => x.Answers)
            .HasConversion(QuizAttemptAnswersJson.Converter, QuizAttemptAnswersJson.Comparer)
            .HasColumnType("jsonb")
            .HasColumnName("answers")
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();

        builder.Property(x => x.ScorePercent)
            .IsRequired()
            .HasColumnName("score_percent");

        builder.Property(x => x.Passed)
            .IsRequired()
            .HasColumnName("passed");

        builder.Property(x => x.SubmittedAt)
            .IsRequired()
            .HasColumnName("submitted_at");

        // НЕ unique: попыток на пару (user, quiz) сколько угодно — best/last на чтении.
        builder.HasIndex(x => new { x.UserId, x.QuizId })
            .HasDatabaseName(USER_QUIZ_INDEX);
    }
}
