using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Postgres.Configurations;

public static class NotificationDeliveriesIndex
{
    public const string NOTIFICATION = "ix_notification_deliveries_notification";
    public const string FK = "fk_notification_deliveries_notification";

    /// <summary>
    /// Уникальность доставки на пару (notification_id, channel). Один канал —
    /// одна запись, retry'и того же event'a делают UPDATE через handler-upsert.
    /// </summary>
    public const string NOTIFICATION_CHANNEL_UNIQUE = "ux_notification_deliveries_notification_channel";

    /// <summary>
    /// Btree index по <c>provider_message_id</c> — Unisender webhook резолвит delivery
    /// по job_id (`WHERE provider_message_id = @jobId`). Без индекса seq scan по всей
    /// таблице, которая растёт с трафиком (issue #230, DB-2).
    /// </summary>
    public const string PROVIDER_MESSAGE_ID = "ix_notification_deliveries_provider_message_id";
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => NotificationDeliveryId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.Property(x => x.NotificationId)
            .HasConversion(
                v => v.Value,
                v => NotificationId.Of(v))
            .HasColumnName("notification_id")
            .IsRequired();

        builder.Property(x => x.Channel)
            .HasConversion<short>()
            .HasColumnName("channel")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<short>()
            .HasColumnName("status")
            .IsRequired();

        builder.Property(x => x.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasMaxLength(NotificationDelivery.PROVIDER_MESSAGE_ID_MAX_LENGTH);

        builder.Property(x => x.ErrorCode)
            .HasColumnName("error_code")
            .HasMaxLength(NotificationDelivery.ERROR_CODE_MAX_LENGTH);

        builder.Property(x => x.ErrorDetail)
            .HasColumnName("error_detail")
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.CompletedAt)
            .HasColumnName("completed_at");

        builder.HasIndex(x => x.NotificationId)
            .HasDatabaseName(NotificationDeliveriesIndex.NOTIFICATION);

        builder.HasIndex(x => x.ProviderMessageId)
            .HasFilter("provider_message_id IS NOT NULL")
            .HasDatabaseName(NotificationDeliveriesIndex.PROVIDER_MESSAGE_ID);

        // Уникальность пары (notification_id, channel) — обязательна для idempotent
        // upsert в TelegramDeliveryRecordedHandler. Без неё две concurrent retry-ки
        // event'a одновременно проходят `GetForNotificationAsync` → null, обе делают
        // INSERT → дубль строк. С constraint'ом второй INSERT падает unique-violation,
        // handler переходит в UPDATE-ветку (см. NotificationErrors.DeliveryAlreadyExists).
        builder.HasIndex(x => new { x.NotificationId, x.Channel })
            .IsUnique()
            .HasDatabaseName(NotificationDeliveriesIndex.NOTIFICATION_CHANNEL_UNIQUE);

        builder.HasOne<Notification>()
            .WithMany()
            .HasForeignKey(x => x.NotificationId)
            .HasConstraintName(NotificationDeliveriesIndex.FK)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
