using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBotService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RotateChatBindingHealthChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_chat_bindings_last_validated_id",
                schema: "telegrambot",
                table: "chat_bindings",
                columns: new[] { "last_validated_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_chat_bindings_last_validated_id",
                schema: "telegrambot",
                table: "chat_bindings");
        }
    }
}
