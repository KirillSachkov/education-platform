using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.UserOptOuts;

namespace NotificationService.Infrastructure.Postgres.Configurations;

public sealed class UserNotificationTypeOptOutConfiguration : IEntityTypeConfiguration<UserNotificationTypeOptOut>
{
    public void Configure(EntityTypeBuilder<UserNotificationTypeOptOut> builder)
    {
        builder.ToTable("user_notification_type_optouts");

        builder.HasKey(x => new { x.UserId, x.Type });

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.Type)
            .HasColumnName("type")
            .HasConversion<short>()
            .IsRequired();

        builder.Property(x => x.OptedOutAt)
            .HasColumnName("opted_out_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();
    }
}
