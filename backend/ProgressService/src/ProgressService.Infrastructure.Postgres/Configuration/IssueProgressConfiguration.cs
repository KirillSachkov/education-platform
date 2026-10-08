using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Issues;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class IssueProgressConfiguration : IEntityTypeConfiguration<IssueProgress>
{
    public const string ENROLLMENT_ISSUE_INDEX = "ux_issue_progress_enrollment_id_issue_id";
    public const string ENROLLMENT_PROJECT_INDEX = "ix_issue_progress_enrollment_id_project_id";
    public const string ENROLLMENT_FOREIGN_KEY = "fk_issue_progress_course_enrollments_enrollment_id";

    public void Configure(EntityTypeBuilder<IssueProgress> builder)
    {
        builder.ToTable("issue_progress");

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

        builder.Property(x => x.IssueId)
            .IsRequired()
            .HasColumnName("issue_id");

        builder.Property(x => x.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("status");

        builder.Property(x => x.StartedAt)
            .HasColumnName("started_at");

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        builder.HasIndex(x => new { x.EnrollmentId, x.IssueId })
            .HasDatabaseName(ENROLLMENT_ISSUE_INDEX)
            .IsUnique();

        builder.HasIndex(x => new { x.EnrollmentId, x.ProjectId })
            .HasDatabaseName(ENROLLMENT_PROJECT_INDEX);
    }
}
