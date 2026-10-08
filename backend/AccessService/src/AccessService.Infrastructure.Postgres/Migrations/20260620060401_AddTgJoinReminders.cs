using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTgJoinReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tg_join_reminders",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reminders_sent = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_reminded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tg_join_reminders", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tg_join_reminders_due",
                schema: "access",
                table: "tg_join_reminders",
                columns: new[] { "completed_at", "reminders_sent", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_tg_join_reminders_user_plan",
                schema: "access",
                table: "tg_join_reminders",
                columns: new[] { "user_id", "plan_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tg_join_reminders",
                schema: "access");
        }
    }
}
