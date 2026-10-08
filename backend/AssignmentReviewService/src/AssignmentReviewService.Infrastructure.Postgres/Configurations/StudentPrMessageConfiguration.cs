using AssignmentReviewService.Domain.Reviews;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class StudentPrMessageConfiguration : IEntityTypeConfiguration<StudentPrMessage>
{
    public void Configure(EntityTypeBuilder<StudentPrMessage> b)
    {
        b.ToTable("student_pr_messages");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(x => x.AiReviewId)
            .HasColumnName("ai_review_id")
            .IsRequired();

        b.Property(x => x.GitHubCommentId)
            .HasColumnName("github_comment_id")
            .IsRequired();

        b.Property(x => x.InReplyToGitHubId)
            .HasColumnName("in_reply_to_github_id");

        b.Property(x => x.Kind)
            .HasColumnName("kind")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.AuthorGithubLogin)
            .HasColumnName("author_github_login")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.Body)
            .HasColumnName("body")
            .IsRequired();

        b.Property(x => x.Path)
            .HasColumnName("path")
            .HasMaxLength(500);

        b.Property(x => x.Line)
            .HasColumnName("line");

        b.Property(x => x.CommentUrl)
            .HasColumnName("comment_url")
            .HasMaxLength(500)
            .IsRequired();

        b.Property(x => x.CreatedAtGithub)
            .HasColumnName("created_at_github")
            .IsRequired();

        b.Property(x => x.IngestedAt)
            .HasColumnName("ingested_at")
            .IsRequired();

        b.Property(x => x.AnsweredAt)
            .HasColumnName("answered_at");

        b.Property(x => x.AnswerBody)
            .HasColumnName("answer_body");

        b.Property(x => x.AnswerGitHubCommentId)
            .HasColumnName("answer_github_comment_id");

        // Идемпотентность ingest'а — GitHub comment id UNIQUE. Повторная доставка того же
        // webhook'а отбивается на DB-уровне (плюс pre-check ExistsByGitHubCommentIdAsync).
        b.HasIndex(x => x.GitHubCommentId)
            .IsUnique()
            .HasDatabaseName("uq_student_pr_messages_github_comment_id");

        b.HasIndex(x => x.AiReviewId)
            .HasDatabaseName("ix_student_pr_messages_ai_review_id");

        b.HasIndex(x => new { x.AiReviewId, x.CreatedAtGithub })
            .HasDatabaseName("ix_student_pr_messages_review_created");

        // FK на ai_reviews — сообщения уходят вместе с проверкой (issue.hard_deleted cascade
        // сносит ai_reviews → сюда каскадит через ON DELETE CASCADE).
        b.HasOne<AiReview>()
            .WithMany()
            .HasForeignKey(x => x.AiReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
