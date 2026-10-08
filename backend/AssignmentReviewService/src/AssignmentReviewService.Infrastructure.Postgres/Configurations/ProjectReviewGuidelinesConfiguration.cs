using AssignmentReviewService.Domain.Reviews;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class ProjectReviewGuidelinesConfiguration : IEntityTypeConfiguration<ProjectReviewGuidelines>
{
    public void Configure(EntityTypeBuilder<ProjectReviewGuidelines> b)
    {
        b.ToTable("project_review_guidelines");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        b.Property(x => x.AuthorId)
            .HasColumnName("author_id")
            .IsRequired();

        b.Property(x => x.GuidelinesMarkdown)
            .HasColumnName("guidelines_markdown")
            .HasColumnType("text")
            .IsRequired();

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // 1 guidelines snapshot per project.
        b.HasIndex(x => x.ProjectId)
            .IsUnique()
            .HasDatabaseName("uq_project_review_guidelines_project");
    }
}
