using AccessService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        b.Property(x => x.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        b.Property(x => x.AmountCents)
            .HasColumnName("amount_cents")
            .IsRequired();

        b.Property(x => x.Currency)
            .HasColumnName("currency")
            .HasMaxLength(Order.CURRENCY_MAX_LENGTH)
            .IsRequired();

        b.Property(x => x.ExternalProviderRef)
            .HasColumnName("external_provider_ref")
            .HasMaxLength(Order.EXTERNAL_REF_MAX_LENGTH);

        b.Property(x => x.Provider)
            .HasColumnName("provider")
            .HasMaxLength(Order.PROVIDER_MAX_LENGTH);

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        b.Property(x => x.PaidAt)
            .HasColumnName("paid_at");

        b.Property(x => x.FailureReason)
            .HasColumnName("failure_reason")
            .HasMaxLength(Order.FAILURE_REASON_MAX_LENGTH);

        b.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(Order.CORRELATION_ID_MAX_LENGTH);

        // Recurring auto-renew (#614): тип списания + recurring-токен провайдера.
        b.Property(x => x.ChargeType)
            .HasColumnName("charge_type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(OrderChargeType.INITIAL)
            .IsRequired();

        b.Property(x => x.RebillId)
            .HasColumnName("rebill_id")
            .HasMaxLength(Order.REBILL_ID_MAX_LENGTH);

        b.Property(x => x.RenewalGrantId).HasColumnName("renewal_grant_id");
        b.Property(x => x.RenewalPreviousExpiresAt).HasColumnName("renewal_previous_expires_at");
        b.Property(x => x.RenewalTargetExpiresAt).HasColumnName("renewal_target_expires_at");

        b.HasIndex(x => x.UserId).HasDatabaseName("ix_orders_user_id");
        b.HasIndex(x => x.PlanId).HasDatabaseName("ix_orders_plan_id");
        b.HasIndex(x => new { x.Status, x.Provider, x.CreatedAt })
            .HasDatabaseName("ix_orders_status_provider_created");
        b.HasIndex(x => new { x.Status, x.ChargeType, x.Provider, x.RebillId, x.CreatedAt })
            .HasDatabaseName("ix_orders_rebill_recovery");
        b.HasIndex(x => new { x.Status, x.ChargeType, x.Provider, x.CreatedAt })
            .HasDatabaseName("ix_orders_renewal_refund_reconciliation");

        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<PlanGrant>()
            .WithMany()
            .HasForeignKey(x => x.RenewalGrantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Unique partial index по external_provider_ref для idempotency на webhook retry'ях.
        // Partial — потому что PENDING orders ещё не имеют external ref'а (NULL'ы не unique'ятся).
        b.HasIndex(x => x.ExternalProviderRef)
            .IsUnique()
            .HasFilter("external_provider_ref IS NOT NULL")
            .HasDatabaseName("ux_orders_external_provider_ref");

        // Admin-поиск по correlation_id (#443). Partial — NULL'ы фоновых заказов не индексируем.
        b.HasIndex(x => x.CorrelationId)
            .HasFilter("correlation_id IS NOT NULL")
            .HasDatabaseName("ix_orders_correlation_id");

        // EF Core concurrency token через PostgreSQL system column `xmin`.
        // Колонка не добавляется явно в DDL — `xmin` есть на каждой таблице.
        // При concurrent UPDATE один из транзакций получит DbUpdateConcurrencyException
        // → Wolverine retry handler (см. docs/agents/wolverine-tests.md).
        b.Property<uint>("xmin").IsRowVersion();
    }
}
