using System.Text.Json;
using EducationContentService.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public sealed class ProjectReviewContextConfiguration : IEntityTypeConfiguration<ProjectReviewContext>
{
    public void Configure(EntityTypeBuilder<ProjectReviewContext> b)
    {
        b.ToTable("project_review_contexts");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.ProjectId)
            .HasColumnName("project_id")
            .IsRequired();

        b.Property(x => x.GuidelinesMarkdown)
            .HasColumnName("guidelines_markdown")
            .HasMaxLength(ProjectReviewContext.GUIDELINES_MAX_LENGTH)
            .IsRequired();

        b.Property(x => x.IsAutoReviewEnabled)
            .HasColumnName("is_auto_review_enabled")
            .IsRequired();

        b.Property(x => x.RequiresGithubConnection)
            .HasColumnName("requires_github_connection")
            .IsRequired();

        b.Property(x => x.RequiresReviewApp)
            .HasColumnName("requires_review_app")
            .IsRequired();

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        // 1:0..1 с Project: один контекст на проект.
        b.HasIndex(x => x.ProjectId)
            .IsUnique()
            .HasDatabaseName("uq_project_review_contexts_project");
    }
}
