using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Projects;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class ProjectProgressConfiguration : IEntityTypeConfiguration<ProjectProgress>
{
    public const string ENROLLMENT_PROJECT_INDEX = "ux_project_progress_enrollment_id_project_id";
    public const string ENROLLMENT_FOREIGN_KEY = "fk_project_progress_course_enrollments_enrollment_id";

    public void Configure(EntityTypeBuilder<ProjectProgress> builder)
    {
        builder.ToTable("project_progress");

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

        builder.Property(x => x.ProjectId)
            .IsRequired()
            .HasColumnName("project_id");

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("status");

        builder.Property(x => x.TotalIssuesCount)
            .IsRequired()
            .HasColumnName("total_issues_count");

        builder.Property(x => x.TotalIssuesCompleted)
            .IsRequired()
            .HasColumnName("total_issues_completed");

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.HasIndex(x => new { x.EnrollmentId, x.ProjectId })
            .HasDatabaseName(ENROLLMENT_PROJECT_INDEX)
            .IsUnique();
    }
}
