using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class ProjectsIndex
{
    public const string TITLE = "ix_projects_title";
    public const string AUTHOR_ID = "ix_projects_author_id";
}

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .IsRequired()
            .HasColumnName("id");

        builder.Property(x => x.AuthorId)
            .IsRequired()
            .HasColumnName("author_id");

        builder.Property(x => x.Title)
            .HasConversion(
                v => v.Value,
                v => Title.Create(v).Value)
            .HasMaxLength(Title.MAX_LENGTH)
            .HasColumnName("title")
            .IsRequired();

        builder.Property(x => x.Description)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : Description.Create(v).Value)
            .HasMaxLength(Description.MAX_LENGTH)
            .HasColumnName("description")
            .IsRequired(false);

        builder.Property(x => x.DetailedDescription)
            .HasConversion(
                v => v == null ? null : v.Value,
                v => v == null ? null : DetailedDescription.Create(v).Value)
            .HasColumnName("detailed_description")
            .IsRequired(false);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.HasIndex(x => x.Title).IsUnique().HasFilter("status <> 'DRAFT'").HasDatabaseName(ProjectsIndex.TITLE);
        builder.HasIndex(x => x.AuthorId).HasDatabaseName(ProjectsIndex.AUTHOR_ID);

        builder.HasMany<ProjectItem>()
            .WithOne()
            .HasForeignKey(pi => pi.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
