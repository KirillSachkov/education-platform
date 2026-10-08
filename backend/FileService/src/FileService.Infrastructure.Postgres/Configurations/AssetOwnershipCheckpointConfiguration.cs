using FileService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FileService.Infrastructure.Postgres.Configurations;

public sealed class AssetOwnershipCheckpointConfiguration : IEntityTypeConfiguration<AssetOwnershipCheckpoint>
{
    public void Configure(EntityTypeBuilder<AssetOwnershipCheckpoint> builder)
    {
        builder.ToTable("asset_ownership_checkpoints");
        builder.HasKey(x => new { x.CourseId, x.TargetType, x.TargetId });

        builder.Property(x => x.CourseId)
            .HasColumnName("course_id")
            .ValueGeneratedNever();

        builder.Property(x => x.TargetType)
            .HasColumnName("target_type")
            .HasMaxLength(TargetEntity.MAX_TYPE_LENGTH)
            .IsRequired();

        builder.Property(x => x.TargetId)
            .HasColumnName("target_id")
            .ValueGeneratedNever();

        builder.Property(x => x.DesiredOwnerId)
            .HasColumnName("desired_owner_id")
            .ValueGeneratedNever();

        builder.Property(x => x.LastAppliedRevision)
            .HasColumnName("last_applied_revision")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.HasIndex(x => new { x.TargetType, x.TargetId, x.LastAppliedRevision })
            .IsDescending(false, false, true);
    }
}
