using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.UserChannels;

namespace NotificationService.Infrastructure.Postgres.Configurations;

public sealed class UserNotificationChannelsConfiguration : IEntityTypeConfiguration<UserNotificationChannels>
{
    public void Configure(EntityTypeBuilder<UserNotificationChannels> builder)
    {
        builder.ToTable("user_notification_channels");

        builder.HasKey(x => x.UserId);

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.TelegramEnabled)
            .HasColumnName("telegram_enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.EmailEnabled)
            .HasColumnName("email_enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.WebPushEnabled)
            .HasColumnName("web_push_enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();
    }
}
