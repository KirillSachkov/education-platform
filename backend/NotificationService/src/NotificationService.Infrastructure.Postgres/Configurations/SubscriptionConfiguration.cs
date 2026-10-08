using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotificationService.Domain.Subscriptions;

namespace NotificationService.Infrastructure.Postgres.Configurations;

public static class SubscriptionsIndex
{
    public const string USER_ENTITY_UNIQUE = "ux_subscriptions_user_entity";
    public const string ENTITY_USER = "ix_subscriptions_entity_user";
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                v => v.Value,
                v => SubscriptionId.Of(v))
            .HasColumnName("id")
            .IsRequired();

        builder.Property(x => x.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasColumnName("entity_type")
            .HasMaxLength(SubscriptionEntityType.MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.EntityId)
            .HasColumnName("entity_id")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.HasIndex(x => new { x.UserId, x.EntityType, x.EntityId })
            .IsUnique()
            .HasDatabaseName(SubscriptionsIndex.USER_ENTITY_UNIQUE);

        builder.HasIndex(x => new { x.EntityType, x.EntityId, x.UserId })
            .HasDatabaseName(SubscriptionsIndex.ENTITY_USER);
    }
}
