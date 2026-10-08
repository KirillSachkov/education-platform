using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.CoursePositions;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class CoursePositionConfiguration : IEntityTypeConfiguration<CoursePosition>
{
    public const string USER_COURSE_INDEX = "ux_course_positions_user_id_course_id";

    public void Configure(EntityTypeBuilder<CoursePosition> builder)
    {
        builder.ToTable("course_positions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.CourseId)
            .IsRequired()
            .HasColumnName("course_id");

        builder.Property(x => x.EntityType)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("entity_type");

        builder.Property(x => x.EntityId)
            .IsRequired()
            .HasColumnName("entity_id");

        builder.Property(x => x.OpenedAt)
            .IsRequired()
            .HasColumnName("opened_at");

        builder.Property(x => x.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        // Один ряд на (user, course) — каждое открытие перезаписывает.
        builder.HasIndex(x => new { x.UserId, x.CourseId })
            .HasDatabaseName(USER_COURSE_INDEX)
            .IsUnique();
    }
}
