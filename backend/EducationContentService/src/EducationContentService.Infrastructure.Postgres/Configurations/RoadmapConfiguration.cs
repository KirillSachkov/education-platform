using EducationContentService.Domain.Roadmaps;
using EducationContentService.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public static class RoadmapsIndex
{
    public const string COURSE_ID_UNIQUE = "ix_roadmaps_course_id_unique";
    public const string SLUG_UNIQUE = "ix_roadmaps_slug_unique";
    public const string AUTHOR_ID = "ix_roadmaps_author_id";
}

public class RoadmapConfiguration : IEntityTypeConfiguration<Roadmap>
{
    public void Configure(EntityTypeBuilder<Roadmap> builder)
    {
        builder.ToTable("roadmaps");

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
                v => v!.Value,
                v => Description.Create(v).Value)
            .HasMaxLength(Description.MAX_LENGTH)
            .HasColumnName("description")
            .IsRequired(false);

        builder.Property(x => x.CourseId)
            .HasColumnName("course_id")
            .IsRequired(false);

        builder.Property(x => x.Slug)
            .HasMaxLength(200)
            .HasColumnName("slug")
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

        builder.HasMany(x => x.Nodes)
            .WithOne()
            .HasForeignKey(n => n.RoadmapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Edges)
            .WithOne()
            .HasForeignKey(e => e.RoadmapId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CourseId)
            .IsUnique()
            .HasFilter("course_id IS NOT NULL")
            .HasDatabaseName(RoadmapsIndex.COURSE_ID_UNIQUE);

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasFilter("slug IS NOT NULL")
            .HasDatabaseName(RoadmapsIndex.SLUG_UNIQUE);

        builder.HasIndex(x => x.AuthorId)
            .HasDatabaseName(RoadmapsIndex.AUTHOR_ID);
    }
}
