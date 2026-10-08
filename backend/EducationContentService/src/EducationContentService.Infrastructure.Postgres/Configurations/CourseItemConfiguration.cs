using EducationContentService.Domain.Courses;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

public class CourseItemConfiguration : IEntityTypeConfiguration<CourseItem>
{
    public void Configure(EntityTypeBuilder<CourseItem> builder)
    {
        builder.ToTable("course_items");

        builder.HasKey(ci => ci.Id);

        builder.Property(ci => ci.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(ci => ci.CourseId)
            .IsRequired()
            .HasColumnName("course_id");

        builder.Property(ci => ci.ItemType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasColumnName("item_type")
            .IsRequired();

        builder.Property(ci => ci.ReferenceId)
            .IsRequired()
            .HasColumnName("reference_id");

        builder.Property(ci => ci.SortKey)
            .HasConversion(
                v => v.Value,
                v => SortKey.Create(v).Value)
            .IsRequired()
            .HasMaxLength(200)
            .UseCollation("C")
            .HasColumnName("sort_key");

        builder.Property(ci => ci.IsOptional)
            .IsRequired()
            .HasColumnName("is_optional");

        builder.HasIndex(ci => new { ci.CourseId, ci.SortKey });
        builder.HasIndex(ci => new { ci.ReferenceId, ci.ItemType, ci.CourseId });
    }
}
