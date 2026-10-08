using AccessService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class PlanGrantConfiguration : IEntityTypeConfiguration<PlanGrant>
{
    public void Configure(EntityTypeBuilder<PlanGrant> b)
    {
        b.ToTable("plan_grants");

        b.HasKey(x => x.Id);

        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        b.Property(x => x.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        b.Property(x => x.Source)
            .HasColumnName("source")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        b.Property(x => x.SourceRef).HasColumnName("source_ref");

        b.Property(x => x.GrantedAt)
            .HasColumnName("granted_at")
            .IsRequired();

        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");

        b.Property(x => x.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        b.Property(x => x.RevokedAt).HasColumnName("revoked_at");

        b.Property(x => x.RevokedBy).HasColumnName("revoked_by");

        b.Property(x => x.RevokeReason)
            .HasColumnName("revoke_reason")
            .HasMaxLength(500);

        // Phase 2 #112: Снимок цены на момент выдачи grant'а. Используется
        // UpgradeCreditCalculator для расчёта скидки при апгрейде на более широкий план.
        b.Property(x => x.PricePaidCents).HasColumnName("price_paid_cents");

        b.Property(x => x.UpgradeBasePriceCents).HasColumnName("upgrade_base_price_cents");

        b.Property(x => x.CreditOverrideUntil).HasColumnName("credit_override_until");
        b.Property(x => x.ExpiryReminderSentAt).HasColumnName("expiry_reminder_sent_at");

        // Recurring auto-renew subscription fields (#614). Nullable — заполняются только для
        // подписочных grants после первого успешного списания (AttachRecurring).
        b.Property(x => x.RebillId)
            .HasColumnName("rebill_id")
            .HasMaxLength(PlanGrant.RECURRING_REF_MAX_LENGTH);

        b.Property(x => x.CustomerKey)
            .HasColumnName("customer_key")
            .HasMaxLength(PlanGrant.RECURRING_REF_MAX_LENGTH);

        b.Property(x => x.NextChargeAt).HasColumnName("next_charge_at");

        b.Property(x => x.ChargeFailureCount)
            .HasColumnName("charge_failure_count")
            .HasDefaultValue(0)
            .IsRequired();

        b.Property(x => x.RenewalGraceEndsAt)
            .HasColumnName("renewal_grace_ends_at");

        b.Property(x => x.AutoRenewalCancelledAt)
            .HasColumnName("auto_renewal_cancelled_at");

        b.Ignore(x => x.AccessEndsAt);

        // Prevent ExpiredGrantsSweeper from overwriting a concurrently confirmed renewal
        // (and vice versa). The losing save becomes a concurrency failure; webhook returns
        // 500 for provider retry and both sweepers reload state on their next pass.
        b.Property<uint>("xmin").IsRowVersion();

        b.HasIndex(x => new { x.UserId, x.Status }).HasDatabaseName("ix_plan_grants_user_status");
        b.HasIndex(x => x.PlanId).HasDatabaseName("ix_plan_grants_plan_id");

        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

        // Non-unique composite (user_id, plan_id) — hot path для
        // IssueAutoFreeGrantsOnUser{Created,LoggedIn}Handler.ExistsAsync на каждом логине.
        // Партиальный unique-индекс `uq_plan_grants_user_plan_active` ниже
        // НЕ используется для query без фильтра по status — нужен полный композит.
        // Создаётся через raw SQL в миграции `AddPlanGrantsUserPlanIndex` — EF Core
        // дедуплицирует HasIndex по одинаковому набору колонок с существующим
        // partial-unique, поэтому из Fluent API не задаётся. Issue #228 MSG-1.

        // Партиальный unique-индекс защищает от race-условий: на retry UserCreated
        // (или любого другого источника grant'ов) handler делает check-then-act
        // ExistsAsync → AddAsync, две параллельных tx прошли проверку и пытаются
        // вставить дубликат. БД блокирует второй INSERT, handler ловит unique
        // violation и трактует как идемпотентность. Только ACTIVE — после revoke
        // допустимо новое grant'ование.
        b.HasIndex(x => new { x.UserId, x.PlanId })
            .HasFilter("status = 'ACTIVE'")
            .IsUnique()
            .HasDatabaseName("uq_plan_grants_user_plan_active");
    }
}
