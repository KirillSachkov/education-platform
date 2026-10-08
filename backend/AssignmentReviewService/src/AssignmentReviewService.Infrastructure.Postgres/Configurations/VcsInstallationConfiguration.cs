using System.Text.Json;
using AssignmentReviewService.Domain.Vcs;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PlatformDatabase;

namespace AssignmentReviewService.Infrastructure.Postgres.Configurations;

internal sealed class VcsInstallationConfiguration : IEntityTypeConfiguration<VcsInstallation>
{
    public void Configure(EntityTypeBuilder<VcsInstallation> b)
    {
        b.ToTable("vcs_installations");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id)
            .HasColumnName("id")
            .HasValueGenerator<TimeOrderedGuidValueGenerator>()
            .ValueGeneratedOnAdd();

        b.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.InstallationId)
            .HasColumnName("installation_id")
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.OwnerType)
            .HasColumnName("owner_type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.OwnerLogin)
            .HasColumnName("owner_login")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.OwnerExternalId)
            .HasColumnName("owner_external_id")
            .HasMaxLength(50)
            .IsRequired();

        b.Property(x => x.LinkedUserId).HasColumnName("linked_user_id");

        ValueComparer<RepoSelections> repoSelectionsComparer = new(
            (a, c) => (a == null && c == null)
                || (a != null && c != null && a.All == c.All && a.Repos.SequenceEqual(c.Repos, StringComparer.Ordinal)),
            v => HashCode.Combine(
                v.All,
                v.Repos.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode(StringComparison.Ordinal)))),
            v => new RepoSelections { All = v.All, Repos = v.Repos.ToList() });

        b.Property(x => x.RepoSelections)
            .HasColumnName("repo_selections")
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<RepoSelections>(v, (JsonSerializerOptions?)null) ?? RepoSelections.Empty())
            .Metadata.SetValueComparer(repoSelectionsComparer);

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.InstalledAt)
            .HasColumnName("installed_at")
            .IsRequired();

        b.Property(x => x.RemovedAt).HasColumnName("removed_at");

        b.HasIndex(x => new { x.Provider, x.InstallationId })
            .IsUnique()
            .HasDatabaseName("uq_vcs_installations_provider_installation");

        b.HasIndex(x => x.LinkedUserId)
            .HasDatabaseName("ix_vcs_installations_linked_user_id");
    }
}
