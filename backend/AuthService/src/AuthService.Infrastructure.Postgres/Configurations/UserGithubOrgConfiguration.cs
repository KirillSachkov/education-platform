using AuthService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuthService.Infrastructure.Postgres.Configurations;

public class UserGithubOrgConfiguration : IEntityTypeConfiguration<UserGithubOrg>
{
    public void Configure(EntityTypeBuilder<UserGithubOrg> builder)
    {
        builder.ToTable("user_github_orgs");

        builder.HasKey(x => new { x.UserId, x.OrgSlug });

        builder.Property(x => x.UserId)
            .IsRequired()
            .HasColumnType("uuid")
            .HasColumnName("user_id");

        builder.Property(x => x.OrgSlug)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("org_slug");

        builder.Property(x => x.SyncedAt)
            .IsRequired()
            .HasColumnName("synced_at");

        builder.HasIndex(x => x.OrgSlug)
            .HasDatabaseName("ix_user_github_orgs_org_slug");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
