using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Modules;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class ModuleItemProgressConfiguration : IEntityTypeConfiguration<ModuleItemProgress>
{
    public const string ENROLLMENT_MODULE_REFERENCE_TYPE_INDEX =
        "ux_module_item_progress_enrollment_id_module_id_reference_id_item_type";

    public const string ENROLLMENT_REFERENCE_TYPE_INDEX =
        "ix_module_item_progress_enrollment_id_reference_id_item_type";

    public const string ENROLLMENT_FOREIGN_KEY = "fk_module_item_progress_course_enrollments_enrollment_id";

    public void Configure(EntityTypeBuilder<ModuleItemProgress> builder)
    {
        builder.ToTable("module_item_progress");

        builder.HasKey(x => x.Id);
        
        builder.HasOne<CourseEnrollment>()
            .WithMany()
            .HasForeignKey(x => x.EnrollmentId)
            .HasConstraintName(ENROLLMENT_FOREIGN_KEY)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.Version)
            .IsRowVersion();

        builder.Property(x => x.EnrollmentId)
            .IsRequired()
            .HasColumnName("enrollment_id");

        builder.Property(x => x.ModuleId)
            .IsRequired()
            .HasColumnName("module_id");

        builder.Property(x => x.ReferenceId)
            .IsRequired()
            .HasColumnName("reference_id");

        builder.Property(x => x.ItemType)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("item_type");

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("status");

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.HasIndex(x => new { x.EnrollmentId, x.ModuleId, x.ReferenceId, x.ItemType })
            .HasDatabaseName(ENROLLMENT_MODULE_REFERENCE_TYPE_INDEX)
            .IsUnique();

        builder.HasIndex(x => new { x.EnrollmentId, x.ReferenceId, x.ItemType })
            .HasDatabaseName(ENROLLMENT_REFERENCE_TYPE_INDEX);
    }
}
