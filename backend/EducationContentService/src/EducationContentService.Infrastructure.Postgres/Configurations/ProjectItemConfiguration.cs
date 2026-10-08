using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Projects.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public class ProjectItemConfiguration : IEntityTypeConfiguration<ProjectItem>
{
    public void Configure(EntityTypeBuilder<ProjectItem> builder)
    {
        builder.ToTable("project_items");

        builder.HasKey(pi => pi.Id);

        builder.Property(pi => pi.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(pi => pi.ProjectId)
            .IsRequired()
            .HasColumnName("project_id");

        builder.Property(pi => pi.IssueId)
            .IsRequired()
            .HasColumnName("issue_id");

        builder.Property(pi => pi.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        builder.Property(pi => pi.IsOptional)
            .IsRequired()
            .HasColumnName("is_optional");

        builder.Property(pi => pi.MaxScore)
            .HasConversion(
                v => v!.Value,
                v => MaxScore.Create(v).Value)
            .IsRequired(false)
            .HasColumnName("max_score");

        builder.HasIndex(pi => new { pi.ProjectId, pi.SortKey });
    }
}
