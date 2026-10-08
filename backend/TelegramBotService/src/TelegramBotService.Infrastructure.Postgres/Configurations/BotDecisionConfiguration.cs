using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelegramBotService.Domain.Audit;

namespace TelegramBotService.Infrastructure.Postgres.Configurations;

public static class BotDecisionsIndex
{
    public const string CREATED_AT = "ix_bot_decisions_created_at";
    public const string CHAT_USER = "ix_bot_decisions_chat_user";
}

public sealed class BotDecisionConfiguration : IEntityTypeConfiguration<BotDecision>
{
    public void Configure(EntityTypeBuilder<BotDecision> builder)
    {
        builder.ToTable("bot_decisions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.TelegramChatId)
            .HasColumnName("telegram_chat_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(x => x.TelegramUserId)
            .HasColumnName("telegram_user_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(x => x.Decision)
            .HasColumnName("decision")
            .HasMaxLength(BotDecision.DECISION_MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasColumnName("reason")
            .HasMaxLength(BotDecision.REASON_MAX_LENGTH);

        builder.Property(x => x.PlanId)
            .HasColumnName("plan_id");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        // Для retention-cleanup и admin-запросов «последние решения по чату/юзеру».
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName(BotDecisionsIndex.CREATED_AT);

        builder.HasIndex(x => new { x.TelegramChatId, x.TelegramUserId, x.CreatedAt })
            .HasDatabaseName(BotDecisionsIndex.CHAT_USER);
    }
}
