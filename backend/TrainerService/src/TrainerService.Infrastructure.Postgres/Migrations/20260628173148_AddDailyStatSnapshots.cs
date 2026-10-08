using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyStatSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_stat_snapshots",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sessions_started = table.Column<int>(type: "integer", nullable: false),
                    active_users = table.Column<int>(type: "integer", nullable: false),
                    completed_sessions = table.Column<int>(type: "integer", nullable: false),
                    open_grades = table.Column<int>(type: "integer", nullable: false),
                    total_cost_micro_rub = table.Column<long>(type: "bigint", nullable: false),
                    avg_accuracy_pct = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_stat_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "question_accuracy_snapshots",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    correct = table.Column<int>(type: "integer", nullable: false),
                    accuracy_pct = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_accuracy_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "topic_mastery_snapshots",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_date = table.Column<DateOnly>(type: "date", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mastery = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_mastery_snapshots", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_daily_stat_snapshots_date",
                schema: "trainer",
                table: "daily_stat_snapshots",
                column: "snapshot_date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_question_accuracy_snapshots_question_date",
                schema: "trainer",
                table: "question_accuracy_snapshots",
                columns: new[] { "question_id", "snapshot_date" });

            migrationBuilder.CreateIndex(
                name: "ux_question_accuracy_snapshots_date_question",
                schema: "trainer",
                table: "question_accuracy_snapshots",
                columns: new[] { "snapshot_date", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_topic_mastery_snapshots_user_date",
                schema: "trainer",
                table: "topic_mastery_snapshots",
                columns: new[] { "user_id", "snapshot_date" });

            migrationBuilder.CreateIndex(
                name: "ux_topic_mastery_snapshots_date_user_topic",
                schema: "trainer",
                table: "topic_mastery_snapshots",
                columns: new[] { "snapshot_date", "user_id", "topic_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_stat_snapshots",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "question_accuracy_snapshots",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "topic_mastery_snapshots",
                schema: "trainer");
        }
    }
}
