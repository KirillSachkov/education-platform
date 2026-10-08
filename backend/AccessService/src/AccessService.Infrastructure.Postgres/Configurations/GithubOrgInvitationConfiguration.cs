using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class GithubOrgInvitationConfiguration : IEntityTypeConfiguration<GithubOrgInvitation>
{
    public void Configure(EntityTypeBuilder<GithubOrgInvitation> b)
    {
        b.ToTable("github_org_invitations");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.PlanId).HasColumnName("plan_id").IsRequired();

        b.Property(x => x.UserId).HasColumnName("user_id").IsRequired();

        b.Property(x => x.GithubLogin)
            .HasColumnName("github_login")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.OrgLogin)
            .HasColumnName("org_login")
            .HasMaxLength(100)
            .IsRequired();

        b.Property(x => x.GithubInvitationId).HasColumnName("github_invitation_id");

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.FailureReason).HasColumnName("failure_reason");

        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(x => x.AcceptedAt).HasColumnName("accepted_at");

        b.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");

        b.HasIndex(x => new { x.PlanId, x.UserId })
            .HasDatabaseName("ix_github_org_invitations_user_plan")
            .IsUnique();

        // Partial — pending lookups для webhook (когда юзер accept'нул).
        b.HasIndex(x => new { x.OrgLogin, x.GithubLogin })
            .HasDatabaseName("ix_github_org_invitations_pending");

        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
