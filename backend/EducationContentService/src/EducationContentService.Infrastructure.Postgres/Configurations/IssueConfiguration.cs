using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Projects.ValueObjects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class IssuesIndex
{
    public const string TITLE = "ix_issues_title";
    public const string PROJECT_ID = "ix_issues_project_id";
    public const string AUTHOR_ID = "ix_issues_author_id";
}

public class IssueConfiguration : IEntityTypeConfiguration<Issue>
{
    public void Configure(EntityTypeBuilder<Issue> builder)
    {
        builder.ToTable("issues");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .IsRequired()
            .HasColumnName("id");

        builder.Property(x => x.AuthorId)
            .IsRequired()
            .HasColumnName("author_id");

        builder.Property(x => x.ProjectId)
            .IsRequired()
            .HasColumnName("project_id");

        builder.Property(x => x.Title)
            .HasConversion(
                v => v.Value,
                v => Title.Create(v).Value)
            .HasMaxLength(Title.MAX_LENGTH)
            .HasColumnName("title")
            .IsRequired();

        builder.Property(x => x.Content)
            .HasConversion(
                v => v.Value,
                v => MarkdownContent.Create(v).Value)
            .HasColumnName("content")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(x => x.AccessType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(AccessType.ENROLLED)
            .HasColumnName("access_type")
            .IsRequired();

        builder.Property(x => x.SubmissionMode)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("submission_mode")
            .IsRequired();

        builder.Property(x => x.SelfCheckInstructions)
            .HasColumnName("self_check_instructions")
            .HasColumnType("text")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasDefaultValueSql("timezone('utc', now())")
            .HasColumnName("updated_at");

        builder.OwnsMany(x => x.ExternalLinks, el =>
        {
            el.ToJson("external_links");

            el.Property(e => e.Url)
                .HasConversion(
                    v => v.Value,
                    v => Url.Create(v).Value);

            el.Property(e => e.Title)
                .HasConversion(
                    v => v.Value,
                    v => Title.Create(v).Value);

            el.Property(e => e.IsRequired);
        });

        builder.OwnsMany(x => x.InternalMaterials, im =>
        {
            im.ToJson("internal_materials");

            im.Property(m => m.ItemType)
                .HasConversion<string>();

            im.Property(m => m.ReferenceId);

            im.Property(m => m.IsRequired);
        });

        builder.HasIndex(x => new { x.ProjectId, x.Title }).IsUnique().HasFilter("status <> 'DRAFT'")
            .HasDatabaseName(IssuesIndex.TITLE);
        builder.HasIndex(x => x.ProjectId).HasDatabaseName(IssuesIndex.PROJECT_ID);
        builder.HasIndex(x => x.AuthorId).HasDatabaseName(IssuesIndex.AUTHOR_ID);
    }
}
