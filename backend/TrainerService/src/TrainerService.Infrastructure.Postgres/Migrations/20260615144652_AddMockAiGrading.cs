using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMockAiGrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ai_overall_feedback",
                schema: "trainer",
                table: "training_sessions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_strengths_json",
                schema: "trainer",
                table: "training_sessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ai_weak_topics_json",
                schema: "trainer",
                table: "training_sessions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "grading_status",
                schema: "trainer",
                table: "training_sessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                // Backfill pre-existing rows with a valid enum member (not EF's empty-string
                // default) — a stray '' would fail to parse back into GradingStatus on read. New
                // rows get NOT_REQUIRED from the domain ctor; this only covers migration backfill.
                defaultValue: "NOT_REQUIRED");

            migrationBuilder.AlterColumn<string>(
                name: "feedback",
                schema: "trainer",
                table: "training_session_items",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ai_overall_feedback",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "ai_strengths_json",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "ai_weak_topics_json",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "grading_status",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.AlterColumn<string>(
                name: "feedback",
                schema: "trainer",
                table: "training_session_items",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);
        }
    }
}
