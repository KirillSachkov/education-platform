using EducationContentService.Domain.Courses;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public class CourseMaterialConfiguration : IEntityTypeConfiguration<CourseMaterial>
{
    public void Configure(EntityTypeBuilder<CourseMaterial> builder)
    {
        builder.ToTable("course_materials");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.CourseId)
            .IsRequired()
            .HasColumnName("course_id");

        builder.Property(x => x.MaterialId)
            .IsRequired()
            .HasColumnName("material_id");

        builder.Property(x => x.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        builder.HasIndex(x => new { x.CourseId, x.SortKey });
        builder.HasIndex(x => new { x.CourseId, x.MaterialId }).IsUnique();
        builder.HasIndex(x => new { x.MaterialId, x.CourseId });
    }
}
