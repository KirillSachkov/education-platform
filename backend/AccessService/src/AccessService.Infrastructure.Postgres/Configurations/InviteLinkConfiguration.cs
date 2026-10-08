using AccessService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class InviteLinkConfiguration : IEntityTypeConfiguration<InviteLink>
{
    public void Configure(EntityTypeBuilder<InviteLink> b)
    {
        b.ToTable("invite_links");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        b.OwnsOne(x => x.Token, tb =>
        {
            tb.Property(t => t.Value)
                .HasColumnName("token")
                .HasMaxLength(InviteToken.LENGTH)
                .IsRequired();

            tb.HasIndex(t => t.Value)
                .IsUnique()
                .HasDatabaseName("ix_invite_links_token");
        });

        b.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        b.Property(x => x.MultiUse)
            .HasColumnName("multi_use")
            .IsRequired();

        b.Property(x => x.MaxUses).HasColumnName("max_uses");

        b.Property(x => x.UsageCount)
            .HasColumnName("usage_count")
            .IsRequired();

        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");

        b.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        b.Property(x => x.Label)
            .HasColumnName("label")
            .HasMaxLength(200);

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        b.HasIndex(x => x.PlanId).HasDatabaseName("ix_invite_links_plan_id");

        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
