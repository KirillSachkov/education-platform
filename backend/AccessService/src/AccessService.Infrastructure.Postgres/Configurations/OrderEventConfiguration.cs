using AccessService.Domain;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AccessService.Infrastructure.Postgres.Configurations;

internal sealed class OrderEventConfiguration : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> b)
    {
        b.ToTable("order_events");
        b.HasKey(e => e.Id);

        b.Property(e => e.Id).HasColumnName("id");
        b.Property(e => e.OrderId).HasColumnName("order_id");
        b.Property(e => e.EventType)
            .HasColumnName("event_type")
            .HasConversion<string>()
            .HasMaxLength(OrderEvent.EVENT_TYPE_MAX_LENGTH)
            .IsRequired();
        b.Property(e => e.PayloadJson)
            .HasColumnName("payload")
            .HasColumnType("jsonb");
        b.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
        b.Property(e => e.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(OrderEvent.CORRELATION_ID_MAX_LENGTH);
        b.Property(e => e.CreatedAt).HasColumnName("created_at");

        // Restrict: audit-rows append-only — Order никогда не удаляется в обычном flow,
        // и если кто-то попытается DELETE FROM orders (cleanup, миграция, скрипт) —
        // FK защитит audit от каскадного сноса. Compliance-критично: чек-история
        // не должна теряться вместе с заказом.
        b.HasOne<Order>()
            .WithMany()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(e => new { e.OrderId, e.CreatedAt })
            .HasDatabaseName("ix_order_events_order_id_created");
        b.HasIndex(e => new { e.OrderId, e.EventType, e.CreatedAt })
            .HasDatabaseName("ix_order_events_reconciliation_deferral");
    }
}
