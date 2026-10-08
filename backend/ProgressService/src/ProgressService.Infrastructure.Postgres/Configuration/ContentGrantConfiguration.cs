using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProgressService.Domain.ContentAccess;

namespace ProgressService.Infrastructure.Postgres.Configuration;

public sealed class ContentGrantConfiguration : IEntityTypeConfiguration<ContentGrant>
{
    public const string USER_RESOURCE_INDEX = "ux_content_grants_user_resource";

    public void Configure(EntityTypeBuilder<ContentGrant> builder)
    {
        builder.ToTable("content_grants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever()
            .HasColumnName("id");

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnName("user_id");

        builder.Property(x => x.ResourceType)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("resource_type");

        builder.Property(x => x.ResourceId)
            .IsRequired()
            .HasColumnName("resource_id");

        builder.Property(x => x.GrantType)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("grant_type");

        builder.Property(x => x.GrantedAt)
            .IsRequired()
            .HasColumnName("granted_at");

        builder.Property(x => x.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(x => x.RevokedAt)
            .HasColumnName("revoked_at");

        builder.HasIndex(x => new { x.UserId, x.ResourceType, x.ResourceId })
            .HasDatabaseName(USER_RESOURCE_INDEX)
            .HasFilter("revoked_at IS NULL")
            .IsUnique();

        builder.Ignore(x => x.IsActive);
    }
}
