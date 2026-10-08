using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TrainerService.Domain.Tracks;

namespace TrainerService.Infrastructure.Postgres.Configurations;

internal sealed class TrackConfiguration : IEntityTypeConfiguration<Track>
{
    public void Configure(EntityTypeBuilder<Track> b)
    {
        b.ToTable("tracks");

        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");

        b.Property(t => t.Slug)
            .HasColumnName("slug")
            .HasMaxLength(Track.SLUG_MAX_LENGTH)
            .IsRequired();

        b.Property(t => t.Title)
            .HasColumnName("title")
            .HasMaxLength(Track.TITLE_MAX_LENGTH)
            .IsRequired();

        b.Property(t => t.Stack)
            .HasColumnName("stack")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        b.Property(t => t.Description)
            .HasColumnName("description")
            .HasMaxLength(Track.DESCRIPTION_MAX_LENGTH);

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
            .HasDatabaseName("ux_tracks_slug");

        b.HasIndex(t => t.SortKey).HasDatabaseName("ix_tracks_sort_key");
    }
}
