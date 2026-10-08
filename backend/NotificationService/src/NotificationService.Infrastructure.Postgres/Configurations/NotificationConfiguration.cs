using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Postgres.Configurations;

public static class NotificationsIndex
{
    public const string CORRELATION_UNIQUE = "ux_notifications_correlation";
    public const string RECIPIENT_CREATED = "ix_notifications_recipient_created";
    public const string RECIPIENT_UNREAD = "ix_notifications_recipient_unread";
    public const string TYPE_CREATED = "ix_notifications_type_created";
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => NotificationId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.Property(x => x.RecipientUserId)
            .HasColumnName("recipient_user_id")
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<short>()
            .HasColumnName("type")
            .IsRequired();

        builder.Property(x => x.TemplateId)
            .HasColumnName("template_id")
            .HasMaxLength(Notification.TEMPLATE_ID_MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasColumnName("title")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Body)
            .HasColumnName("body")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Channels)
            .HasConversion<short>()
            .HasColumnName("channels")
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .IsRequired();

        builder.Property(x => x.CorrelationId)
            .HasColumnName("correlation_id");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.ReadAt)
            .HasColumnName("read_at");

        builder.HasIndex(x => new { x.RecipientUserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName(NotificationsIndex.RECIPIENT_CREATED);

        builder.HasIndex(x => x.RecipientUserId)
            .HasFilter("\"read_at\" IS NULL")
            .HasDatabaseName(NotificationsIndex.RECIPIENT_UNREAD);

        builder.HasIndex(x => new { x.CorrelationId, x.RecipientUserId, x.Type })
            .IsUnique()
            .HasFilter("\"correlation_id\" IS NOT NULL")
            .HasDatabaseName(NotificationsIndex.CORRELATION_UNIQUE);

        // Weekly digest (#532): watermark MAX(created_at) + выборка получателей по типу —
        // оба запроса фильтруют по type, без индекса это seq scan на каждый тик.
        builder.HasIndex(x => new { x.Type, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName(NotificationsIndex.TYPE_CREATED);
    }
}
