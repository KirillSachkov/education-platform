using AssignmentReviewService.Domain.Reviews;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class AiReviewIterationFeedbackConfiguration
    : IEntityTypeConfiguration<AiReviewIterationFeedback>
{
    public void Configure(EntityTypeBuilder<AiReviewIterationFeedback> b)
    {
        b.ToTable("ai_review_iteration_feedback");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(x => x.IterationId)
            .HasColumnName("iteration_id")
            .IsRequired();

        b.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        b.Property(x => x.IsHelpful)
            .HasColumnName("is_helpful")
            .IsRequired();

        b.Property(x => x.Comment)
            .HasColumnName("comment")
            .HasMaxLength(AiReviewIterationFeedback.COMMENT_MAX_LENGTH);

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // Один фидбэк на пару (iteration, user) — повторный submit upsert'ит.
        b.HasIndex(x => new { x.IterationId, x.UserId })
            .IsUnique()
            .HasDatabaseName("uq_ai_review_iteration_feedback_iteration_user");

        b.HasIndex(x => x.IterationId)
            .HasDatabaseName("ix_ai_review_iteration_feedback_iteration");
    }
}
