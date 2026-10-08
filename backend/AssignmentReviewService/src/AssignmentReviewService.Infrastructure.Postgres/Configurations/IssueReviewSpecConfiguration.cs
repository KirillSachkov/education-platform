using AssignmentReviewService.Domain.Reviews;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class IssueReviewSpecConfiguration : IEntityTypeConfiguration<IssueReviewSpec>
{
    public void Configure(EntityTypeBuilder<IssueReviewSpec> b)
    {
        b.ToTable("issue_review_specs");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(x => x.IssueId)
            .HasColumnName("issue_id")
            .IsRequired();

        b.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        b.Property(x => x.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        b.Property(x => x.AuthorPrompt)
            .HasColumnName("author_prompt")
            .HasColumnType("text");

        b.Property(x => x.ReviewAspects)
            .HasColumnName("review_aspects")
            .HasColumnType("text");

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // 1 spec per issue — handler делает upsert по IssueId.
        b.HasIndex(x => x.IssueId)
            .IsUnique()
            .HasDatabaseName("uq_issue_review_specs_issue");

        b.HasIndex(x => x.ProjectId).HasDatabaseName("ix_issue_review_specs_project");
    }
}
