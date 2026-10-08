using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBotService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddChatBindingAnnouncementMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "announcement_message_id",
                schema: "telegrambot",
                table: "chat_bindings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "announcement_message_id",
                schema: "telegrambot",
                table: "chat_bindings");
        }
    }
}
