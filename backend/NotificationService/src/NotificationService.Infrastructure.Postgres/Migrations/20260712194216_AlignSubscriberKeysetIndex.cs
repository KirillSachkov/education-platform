using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AlignSubscriberKeysetIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_entity",
                schema: "notifications",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_entity_user",
                schema: "notifications",
                table: "subscriptions",
                columns: new[] { "entity_type", "entity_id", "user_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_entity_user",
                schema: "notifications",
                table: "subscriptions");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_entity",
                schema: "notifications",
                table: "subscriptions",
                columns: new[] { "entity_type", "entity_id" });
        }
    }
}
