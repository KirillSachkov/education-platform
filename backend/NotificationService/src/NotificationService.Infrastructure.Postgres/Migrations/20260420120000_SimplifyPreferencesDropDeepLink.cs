using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Postgres.Migrations
{
    /// <summary>
    /// Упрощает настройки уведомлений: убирает per-type <c>notification_preferences</c>
    /// и добавляет per-user <c>user_notification_channels(telegram_enabled, email_enabled)</c>.
    /// Также удаляет столбец <c>deep_link</c> — фронт теперь маршрутизирует по (type, payload),
    /// бэкенду знать URL-схему фронта не нужно.
    ///
    /// Data migration: для существующих юзеров seed'им строки в новую таблицу из:
    /// (1) старой <c>notification_preferences</c> — preserve TelegramEnabled, если был включён хоть для одного типа;
    /// (2) уникальных recipient'ов из <c>notifications</c> — на случай, если у юзера были уведомления, но дефолтных preferences не было.
    /// Email=true для всех (новый дефолт); если кто-то опт-аут — выключит в профиле.
    /// </summary>
    public partial class SimplifyPreferencesDropDeepLink : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_notification_channels",
                schema: "notifications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    email_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_notification_channels", x => x.user_id);
                });

            // Seed из старой per-type таблицы — preserve "юзер хоть раз включал Telegram" → telegram_enabled=true.
            migrationBuilder.Sql("""
                INSERT INTO notifications.user_notification_channels
                    (user_id, telegram_enabled, email_enabled, updated_at)
                SELECT
                    user_id,
                    bool_or((channels & 2) <> 0) AS telegram_enabled,
                    true AS email_enabled,
                    timezone('utc', now())
                FROM notifications.notification_preferences
                GROUP BY user_id
                ON CONFLICT (user_id) DO NOTHING;
            """);

            // Seed из notifications для тех юзеров, у которых записи в preferences не было,
            // но они получали уведомления — чтобы dispatcher не fallback'ился каждый раз на дефолты.
            migrationBuilder.Sql("""
                INSERT INTO notifications.user_notification_channels
                    (user_id, telegram_enabled, email_enabled, updated_at)
                SELECT DISTINCT recipient_user_id, false, true, timezone('utc', now())
                FROM notifications.notifications
                ON CONFLICT (user_id) DO NOTHING;
            """);

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "notifications");

            migrationBuilder.DropColumn(
                name: "deep_link",
                schema: "notifications",
                table: "notifications");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "deep_link",
                schema: "notifications",
                table: "notifications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "notifications",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    channels = table.Column<short>(type: "smallint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => new { x.user_id, x.type });
                });

            migrationBuilder.DropTable(
                name: "user_notification_channels",
                schema: "notifications");
        }
    }
}
