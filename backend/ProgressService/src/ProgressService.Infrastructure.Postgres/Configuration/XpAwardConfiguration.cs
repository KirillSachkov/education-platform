using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Gamification;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class XpAwardConfiguration : IEntityTypeConfiguration<XpAward>
{
    public const string ENROLLMENT_FOREIGN_KEY = "fk_xp_awards_course_enrollments_enrollment_id";
    public const string USER_INDEX = "ix_xp_awards_user_id";
    public const string ENROLLMENT_INDEX = "ix_xp_awards_enrollment_id";
    public const string USER_AWARD_SOURCE_INDEX = "ux_xp_awards_user_id_award_type_source_id";

    public void Configure(EntityTypeBuilder<XpAward> builder)
    {
        builder.ToTable("xp_awards");

        builder.HasKey(x => x.Id);

        // enrollment_id nullable: MATERIAL_VIEWED теперь user-scoped и не привязан к enrollment'у.
        // При revoke enrollment'а курсовые XP-записи каскадно удаляются, user-scoped (nullable
        // enrollment_id) остаются — просмотр материала → факт о пользователе.
        builder.HasOne<CourseEnrollment>()
            .WithMany()
            .HasForeignKey(x => x.EnrollmentId)
            .HasConstraintName(ENROLLMENT_FOREIGN_KEY)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.Version)
            .IsRowVersion();

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.EnrollmentId)
            .HasColumnName("enrollment_id");

        builder.Property(x => x.AwardType)
            .IsRequired()
            .HasConversion<string>()
            .HasColumnName("award_type");

        builder.Property(x => x.SourceId)
            .IsRequired()
            .HasColumnName("source_id");

        builder.Property(x => x.XpAmount)
            .IsRequired()
            .HasColumnName("xp_amount");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName(USER_INDEX);

        builder.HasIndex(x => x.EnrollmentId)
            .HasDatabaseName(ENROLLMENT_INDEX);

        builder.HasIndex(x => new { x.UserId, x.AwardType, x.SourceId })
            .HasDatabaseName(USER_AWARD_SOURCE_INDEX)
            .IsUnique();
    }
}
