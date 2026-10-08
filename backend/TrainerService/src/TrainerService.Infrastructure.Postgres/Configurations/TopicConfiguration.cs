using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.Topics;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> b)
    {
        b.ToTable("topics");

        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");

        // NOT NULL column; the domain factory always supplies TrackId for new topics. The
        // ADD COLUMN against the 2 pre-existing demo rows is made safe in the migration via a
        // temporary all-zeros server default that is dropped immediately after (see
        // AddTracksAndTopicDirection.Up). Those throwaway rows read back as Guid.Empty.
        b.Property(t => t.TrackId)
            .HasColumnName("track_id")
            .IsRequired();

        b.Property(t => t.Direction)
            .HasColumnName("direction")
            .HasConversion<string>()
            .HasMaxLength(50);

        b.Property(t => t.Slug)
            .HasColumnName("slug")
            .HasMaxLength(Topic.SLUG_MAX_LENGTH)
            .IsRequired();

        b.Property(t => t.Title)
            .HasColumnName("title")
            .HasMaxLength(Topic.TITLE_MAX_LENGTH)
            .IsRequired();

        b.Property(t => t.Area)
            .HasColumnName("area")
            .HasMaxLength(Topic.AREA_MAX_LENGTH)
            .IsRequired();

        b.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(Topic.DESCRIPTION_MAX_LENGTH);

        b.Property(t => t.RecommendedCourseId).HasColumnName("recommended_course_id");

        b.Property(t => t.FallbackCourseId).HasColumnName("fallback_course_id");

        b.Property(t => t.SortKey)
            .HasColumnName("sort_key")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(t => t.IsPublished)
            .HasColumnName("is_published")
            .IsRequired();

        b.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at").IsRequired();

        b.HasIndex(t => t.Slug)
            .IsUnique()
            .HasDatabaseName("ux_topics_slug");

        b.HasIndex(t => t.TrackId).HasDatabaseName("ix_topics_track_id");
        b.HasIndex(t => t.SortKey).HasDatabaseName("ix_topics_sort_key");
    }
}
