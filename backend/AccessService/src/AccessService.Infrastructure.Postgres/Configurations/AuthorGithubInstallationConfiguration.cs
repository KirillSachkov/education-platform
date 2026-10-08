using AccessService.Domain.Integrations.GitHub;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class AuthorGithubInstallationConfiguration : IEntityTypeConfiguration<AuthorGithubInstallation>
{
    public void Configure(EntityTypeBuilder<AuthorGithubInstallation> b)
    {
        b.ToTable("author_github_installations");

        b.HasKey(x => x.AuthorId);

        b.Property(x => x.AuthorId).HasColumnName("author_id");

        b.Property(x => x.InstallationId)
            .HasColumnName("installation_id")
            .IsRequired();

        b.HasIndex(x => x.InstallationId)
            .HasDatabaseName("ix_author_github_installations_installation_id")
            .IsUnique();

        b.Property(x => x.OrgLogin)
            .HasColumnName("org_login")
            .HasMaxLength(100)
            .IsRequired();

        b.HasIndex(x => x.OrgLogin)
            .HasDatabaseName("ix_author_github_installations_org_login");

        b.Property(x => x.InstalledAt)
            .HasColumnName("installed_at")
            .IsRequired();

        b.Property(x => x.SuspendedAt).HasColumnName("suspended_at");
    }
}
