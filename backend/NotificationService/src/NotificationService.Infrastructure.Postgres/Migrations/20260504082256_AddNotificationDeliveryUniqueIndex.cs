using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-cleanup: до этой миграции уникальности не было; теоретически могли
            // образоваться дубликаты `(notification_id, channel)` (на практике на проде
            // на момент 2026-05-04 их нет, но защищаемся). Оставляем самую свежую запись
            // на пару (Delivered > Failed > Skipped > Pending — по `created_at DESC`).
            migrationBuilder.Sql(@"
                DELETE FROM notifications.notification_deliveries
                WHERE id NOT IN (
                    SELECT DISTINCT ON (notification_id, channel) id
                    FROM notifications.notification_deliveries
                    ORDER BY notification_id, channel, created_at DESC, id DESC
                );
            ");

            migrationBuilder.CreateIndex(
                name: "ux_notification_deliveries_notification_channel",
                schema: "notifications",
                table: "notification_deliveries",
                columns: new[] { "notification_id", "channel" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_notification_deliveries_notification_channel",
                schema: "notifications",
                table: "notification_deliveries");
        }
    }
}
