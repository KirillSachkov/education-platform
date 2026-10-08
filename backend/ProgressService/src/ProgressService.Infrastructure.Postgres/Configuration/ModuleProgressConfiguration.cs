using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Modules;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class ModuleProgressConfiguration : IEntityTypeConfiguration<ModuleProgress>
{
    public const string ENROLLMENT_MODULE_INDEX = "ux_module_progress_enrollment_id_module_id";
    public const string ENROLLMENT_FOREIGN_KEY = "fk_module_progress_course_enrollments_enrollment_id";

    public void Configure(EntityTypeBuilder<ModuleProgress> builder)
    {
        builder.ToTable("module_progress");

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

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("status");

        builder.Property(x => x.ItemsTotal)
            .IsRequired()
            .HasColumnName("items_total");

        builder.Property(x => x.ItemsCompleted)
            .IsRequired()
            .HasColumnName("items_completed");

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.HasIndex(x => new { x.EnrollmentId, x.ModuleId })
            .HasDatabaseName(ENROLLMENT_MODULE_INDEX)
            .IsUnique();
    }
}
