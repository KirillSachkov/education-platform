using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Infrastructure.Postgres.Configurations;

public static class ChatBindingsIndex
{
    public const string PLAN_CHAT_UNIQUE = "ux_chat_bindings_plan_chat";
    public const string TELEGRAM_CHAT = "ix_chat_bindings_telegram_chat";
    public const string PLAN = "ix_chat_bindings_plan";
    public const string LAST_VALIDATED = "ix_chat_bindings_last_validated_id";
}

public sealed class ChatBindingConfiguration : IEntityTypeConfiguration<ChatBinding>
{
    public void Configure(EntityTypeBuilder<ChatBinding> builder)
    {
        builder.ToTable("chat_bindings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(x => x.PlanId)
            .HasColumnName("plan_id")
            .IsRequired();

        builder.Property(x => x.TelegramChatId)
            .HasColumnName("telegram_chat_id")
            .HasColumnType("bigint")
            .IsRequired();

        builder.Property(x => x.ChatType)
            .HasColumnName("chat_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ChatTitle)
            .HasColumnName("chat_title")
            .HasMaxLength(ChatBinding.CHAT_TITLE_MAX_LENGTH);

        builder.Property(x => x.InviteLink)
            .HasColumnName("invite_link")
            .HasMaxLength(ChatBinding.INVITE_LINK_MAX_LENGTH)
            .IsRequired();

        builder.Property(x => x.EnrollmentGrantsMembership)
            .HasColumnName("enrollment_grants_membership")
            .IsRequired();

        builder.Property(x => x.MembershipGrantsEnrollment)
            .HasColumnName("membership_grants_enrollment")
            .IsRequired();

        builder.Property(x => x.AutoKickOnRevoke)
            .HasColumnName("auto_kick_on_revoke")
            .IsRequired();

        builder.Property(x => x.EnforceMembership)
            .HasColumnName("enforce_membership")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("timezone('utc', now())")
            .IsRequired();

        builder.Property(x => x.IsHealthy)
            .HasColumnName("is_healthy")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(x => x.LastValidatedAt)
            .HasColumnName("last_validated_at");

        builder.Property(x => x.LastValidationError)
            .HasColumnName("last_validation_error")
            .HasMaxLength(ChatBinding.VALIDATION_ERROR_MAX_LENGTH);

        builder.HasIndex(x => new { x.PlanId, x.TelegramChatId })
            .IsUnique()
            .HasDatabaseName(ChatBindingsIndex.PLAN_CHAT_UNIQUE);

        builder.Property(x => x.AnnouncementMessageId)
            .HasColumnName("announcement_message_id");

        builder.HasIndex(x => x.TelegramChatId)
            .HasDatabaseName(ChatBindingsIndex.TELEGRAM_CHAT);

        builder.HasIndex(x => x.PlanId)
            .HasDatabaseName(ChatBindingsIndex.PLAN);

        builder.HasIndex(x => new { x.LastValidatedAt, x.Id })
            .HasDatabaseName(ChatBindingsIndex.LAST_VALIDATED);
    }
}
