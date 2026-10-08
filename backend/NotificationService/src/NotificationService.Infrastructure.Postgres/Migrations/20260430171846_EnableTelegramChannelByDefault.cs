using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EnableTelegramChannelByDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "telegram_enabled",
                schema: "notifications",
                table: "user_notification_channels",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: false);

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF to_regclass('auth.user_logins') IS NOT NULL THEN
                        INSERT INTO notifications.user_notification_channels
                            (user_id, telegram_enabled, email_enabled, updated_at)
                        SELECT DISTINCT
                            ul."UserId",
                            true,
                            true,
                            timezone('utc', now())
                        FROM auth.user_logins ul
                        WHERE ul."LoginProvider" = 'Telegram'
                        ON CONFLICT (user_id) DO UPDATE SET
                            telegram_enabled = true,
                            updated_at = EXCLUDED.updated_at
                        WHERE notifications.user_notification_channels.telegram_enabled = false;
                    END IF;
                END $$;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "telegram_enabled",
                schema: "notifications",
                table: "user_notification_channels",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }
    }
}
