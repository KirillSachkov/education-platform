using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Infrastructure.Postgres.Configurations;

public static class UserLinksIndex
{
    public const string PLATFORM_USER_UNIQUE = "ux_user_links_platform_user";
}

public sealed class UserLinkConfiguration : IEntityTypeConfiguration<UserLink>
{
    public void Configure(EntityTypeBuilder<UserLink> builder)
    {
        builder.ToTable("user_links");

        builder.HasKey(x => x.TelegramUserId);

        builder.Property(x => x.TelegramUserId)
            .HasColumnName("telegram_user_id")
            .HasColumnType("bigint")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.PlatformUserId)
            .HasColumnName("platform_user_id")
            .IsRequired();

        builder.Property(x => x.LinkedAt)
            .HasColumnName("linked_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.TelegramUsername)
            .HasColumnName("telegram_username")
            .HasMaxLength(UserLink.TELEGRAM_USERNAME_MAX_LENGTH);

        builder.Property(x => x.BlockedAt)
            .HasColumnName("blocked_at");

        builder.Property(x => x.BlockedReason)
            .HasColumnName("blocked_reason")
            .HasMaxLength(UserLink.BLOCKED_REASON_MAX_LENGTH);

        builder.HasIndex(x => x.PlatformUserId)
            .IsUnique()
            .HasDatabaseName(UserLinksIndex.PLATFORM_USER_UNIQUE);
    }
}
