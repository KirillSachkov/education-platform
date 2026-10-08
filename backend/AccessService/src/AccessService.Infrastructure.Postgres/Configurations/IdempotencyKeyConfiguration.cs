using AccessService.Core.Domain;
using AccessService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public void Configure(EntityTypeBuilder<IdempotencyKey> b)
    {
        b.ToTable("idempotency_keys");
        b.HasKey(k => new { k.UserId, k.Key });

        b.Property(k => k.Key)
            .HasColumnName("key")
            .HasMaxLength(IdempotencyKey.KEY_MAX_LENGTH)
            .IsRequired();
        b.Property(k => k.UserId).HasColumnName("user_id").IsRequired();
        b.Property(k => k.PlanId).HasColumnName("plan_id").IsRequired();
        b.Property(k => k.ExpectedScope)
            .HasColumnName("expected_scope")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        b.Property(k => k.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        b.Property(k => k.OrderId).HasColumnName("order_id").IsRequired();
        b.Property(k => k.ResponseBody)
            .HasColumnName("response_body")
            .HasMaxLength(IdempotencyKey.RESPONSE_MAX_LENGTH)
            .IsRequired(false);
        b.Property(k => k.CreatedAt).HasColumnName("created_at");
        b.Property(k => k.UpdatedAt).HasColumnName("updated_at");

        b.HasIndex(k => k.CreatedAt)
            .HasDatabaseName("ix_idempotency_keys_created");
        b.HasIndex(k => k.OrderId)
            .HasDatabaseName("ix_idempotency_keys_order_id");

        b.HasOne<Order>()
            .WithMany()
            .HasForeignKey(k => k.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
