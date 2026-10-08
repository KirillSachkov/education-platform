using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.AuthorQuestions;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class IssueAuthorQuestionConfiguration : IEntityTypeConfiguration<IssueAuthorQuestion>
{
    public const string USER_ISSUE_INDEX = "ux_issue_author_questions_user_id_issue_id";

    public void Configure(EntityTypeBuilder<IssueAuthorQuestion> builder)
    {
        builder.ToTable("issue_author_questions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.IssueId)
            .IsRequired()
            .HasColumnName("issue_id");

        builder.Property(x => x.Message)
            .IsRequired()
            .HasMaxLength(IssueAuthorQuestion.MESSAGE_MAX_LENGTH)
            .HasColumnName("message");

        builder.Property(x => x.AskedAt)
            .IsRequired()
            .HasColumnName("asked_at");

        // Unique по паре (user, issue) — один вопрос на пользователя-задание (идемпотентность).
        builder.HasIndex(x => new { x.UserId, x.IssueId })
            .HasDatabaseName(USER_ISSUE_INDEX)
            .IsUnique();
    }
}
