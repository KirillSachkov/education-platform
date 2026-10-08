using EducationContentService.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public sealed class ReviewSpecConfiguration : IEntityTypeConfiguration<ReviewSpec>
{
    public void Configure(EntityTypeBuilder<ReviewSpec> b)
    {
        b.ToTable("review_specs");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.IssueId)
            .HasColumnName("issue_id")
            .IsRequired();

        b.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        b.Property(x => x.AuthorPrompt)
            .HasColumnName("author_prompt")
            .HasMaxLength(ReviewSpec.AUTHOR_PROMPT_MAX_LENGTH);

        b.Property(x => x.ReviewAspects)
            .HasColumnName("review_aspects")
            .HasMaxLength(ReviewSpec.REVIEW_ASPECTS_MAX_LENGTH);

        b.Property(x => x.IsAutoReviewEnabled)
            .HasColumnName("is_auto_review_enabled")
            .IsRequired();

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // 1:0..1 с Issue: один spec на задание.
        b.HasIndex(x => x.IssueId)
            .IsUnique()
            .HasDatabaseName("uq_review_specs_issue");

        b.HasIndex(x => x.ProjectId)
            .HasDatabaseName("ix_review_specs_project_id");
    }
}
