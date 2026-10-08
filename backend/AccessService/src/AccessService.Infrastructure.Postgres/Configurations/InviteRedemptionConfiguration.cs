using AccessService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class InviteRedemptionConfiguration : IEntityTypeConfiguration<InviteRedemption>
{
    public void Configure(EntityTypeBuilder<InviteRedemption> b)
    {
        b.ToTable("invite_redemptions");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.InviteLinkId)
            .HasColumnName("invite_link_id")
            .IsRequired();

        b.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        b.Property(x => x.PlanGrantId)
            .HasColumnName("plan_grant_id")
            .IsRequired();

        b.Property(x => x.RedeemedAt)
            .HasColumnName("redeemed_at")
            .IsRequired();

        b.Property(x => x.IpHash)
            .HasColumnName("ip_hash")
            .HasMaxLength(128);

        b.HasIndex(x => new { x.InviteLinkId, x.UserId })
            .IsUnique()
            .HasDatabaseName("ix_invite_redemptions_invite_user");
    }
}
