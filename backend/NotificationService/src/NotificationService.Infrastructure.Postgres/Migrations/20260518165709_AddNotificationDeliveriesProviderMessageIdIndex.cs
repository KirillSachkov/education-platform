using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveriesProviderMessageIdIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_provider_message_id",
                schema: "notifications",
                table: "notification_deliveries",
                column: "provider_message_id",
                filter: "provider_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_deliveries_provider_message_id",
                schema: "notifications",
                table: "notification_deliveries");
        }
    }
}
