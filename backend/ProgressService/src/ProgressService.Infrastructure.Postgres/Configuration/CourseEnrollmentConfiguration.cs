using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Enrollments;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class CourseEnrollmentConfiguration : IEntityTypeConfiguration<CourseEnrollment>
{
    public const string USER_COURSE_INDEX = "ux_course_enrollments_user_id_course_id";
    public const string COURSE_ID_INDEX = "ix_course_enrollments_course_id";
    public const string AUTHOR_ID_INDEX = "ix_course_enrollments_author_id";

    public void Configure(EntityTypeBuilder<CourseEnrollment> builder)
    {
        builder.ToTable("course_enrollments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.Version)
            .IsRowVersion();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.CourseId)
            .IsRequired()
            .HasColumnName("course_id");

        builder.Property(x => x.AuthorId)
            .IsRequired()
            .HasColumnName("author_id");

        builder.Property(x => x.Source)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnName("source");

        builder.Property(x => x.EnrolledAt)
            .IsRequired()
            .HasColumnName("enrolled_at");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(x => x.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.HasIndex(x => new { x.UserId, x.CourseId })
            .HasDatabaseName(USER_COURSE_INDEX)
            .IsUnique();

        builder.HasIndex(x => x.CourseId)
            .HasDatabaseName(COURSE_ID_INDEX);

        builder.HasIndex(x => x.AuthorId)
            .HasDatabaseName(AUTHOR_ID_INDEX);
    }
}
