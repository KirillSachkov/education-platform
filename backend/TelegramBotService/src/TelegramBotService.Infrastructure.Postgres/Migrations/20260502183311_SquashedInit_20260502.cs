using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBotService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SquashedInit_20260502 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "telegrambot");

            migrationBuilder.CreateTable(
                name: "bot_decisions",
                schema: "telegrambot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    decision = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bot_decisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_bindings",
                schema: "telegrambot",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    chat_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    chat_title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    invite_link = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    enrollment_grants_membership = table.Column<bool>(type: "boolean", nullable: false),
                    membership_grants_enrollment = table.Column<bool>(type: "boolean", nullable: false),
                    auto_kick_on_revoke = table.Column<bool>(type: "boolean", nullable: false),
                    enforce_membership = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    is_healthy = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_validated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_validation_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chat_bindings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_links",
                schema: "telegrambot",
                columns: table => new
                {
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_links", x => x.telegram_user_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bot_decisions_chat_user",
                schema: "telegrambot",
                table: "bot_decisions",
                columns: new[] { "telegram_chat_id", "telegram_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bot_decisions_created_at",
                schema: "telegrambot",
                table: "bot_decisions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_chat_bindings_course",
                schema: "telegrambot",
                table: "chat_bindings",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_chat_bindings_telegram_chat",
                schema: "telegrambot",
                table: "chat_bindings",
                column: "telegram_chat_id");

            migrationBuilder.CreateIndex(
                name: "ux_chat_bindings_course_chat",
                schema: "telegrambot",
                table: "chat_bindings",
                columns: new[] { "course_id", "telegram_chat_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_links_platform_user",
                schema: "telegrambot",
                table: "user_links",
                column: "platform_user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_decisions",
                schema: "telegrambot");

            migrationBuilder.DropTable(
                name: "chat_bindings",
                schema: "telegrambot");

            migrationBuilder.DropTable(
                name: "user_links",
                schema: "telegrambot");
        }
    }
}
