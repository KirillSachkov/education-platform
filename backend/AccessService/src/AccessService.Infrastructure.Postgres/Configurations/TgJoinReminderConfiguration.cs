using AccessService.Domain;
using AccessService.Domain.TgJoinReminders;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class TgJoinReminderConfiguration : IEntityTypeConfiguration<TgJoinReminder>
{
    public void Configure(EntityTypeBuilder<TgJoinReminder> b)
    {
        b.ToTable("tg_join_reminders");

        b.HasKey(x => x.Id);

        // Aggregate root saved via DbSet.AddAsync — PK генерируется в factory
        // (Guid.CreateVersion7), без ValueGenerator. См. backend-transactions.md правило 4.
        b.Property(x => x.Id).HasColumnName("id");

        b.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        b.Property(x => x.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        b.Property(x => x.GrantId)
            .HasColumnName("grant_id")
            .IsRequired();

        b.Property(x => x.RemindersSent)
            .HasColumnName("reminders_sent")
            .HasDefaultValue(0)
            .IsRequired();

        b.Property(x => x.LastRemindedAt)
            .HasColumnName("last_reminded_at");

        b.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        b.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // Один трекер на пару (user, plan) — on-grant upsert полагается на этот unique.
        b.HasIndex(x => new { x.UserId, x.PlanId })
            .IsUnique()
            .HasDatabaseName("uq_tg_join_reminders_user_plan");

        // Sweeper-предикат: активные строки, готовые к напоминанию (фильтр + bounded batch).
        b.HasIndex(x => new { x.CompletedAt, x.RemindersSent, x.CreatedAt })
            .HasDatabaseName("ix_tg_join_reminders_due");

        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(x => x.PlanId)
            .OnDelete(DeleteBehavior.Restrict);

    }
}
