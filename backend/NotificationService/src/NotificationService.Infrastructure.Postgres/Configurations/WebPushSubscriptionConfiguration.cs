using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.WebPush;

namespace NotificationService.Infrastructure.Postgres.Configurations;

public static class WebPushSubscriptionsIndex
{
    public const string ENDPOINT_UNIQUE = "ux_web_push_subscriptions_endpoint";
    public const string USER = "ix_web_push_subscriptions_user";
}

public sealed class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> builder)
    {
        builder.ToTable("web_push_subscriptions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => WebPushSubscriptionId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        // Push endpoint'ы бывают длинными (FCM/Mozilla) — text без ограничения.
        builder.Property(x => x.Endpoint)
            .HasColumnName("endpoint")
            .IsRequired();

        builder.Property(x => x.P256dh)
            .HasColumnName("p256dh")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Auth)
            .HasColumnName("auth")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(512);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.LastSeenAt)
            .HasColumnName("last_seen_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        // Endpoint глобально уникален — один и тот же браузер не должен иметь две строки.
        builder.HasIndex(x => x.Endpoint)
            .IsUnique()
            .HasDatabaseName(WebPushSubscriptionsIndex.ENDPOINT_UNIQUE);

        // Доставка и чтение настроек идут по user_id.
        builder.HasIndex(x => x.UserId)
            .HasDatabaseName(WebPushSubscriptionsIndex.USER);
    }
}
