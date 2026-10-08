using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropReferenceLinksAndTriageSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reference_links",
                schema: "assignment_review",
                table: "issue_review_specs");

            migrationBuilder.DropColumn(
                name: "triage_max_output_tokens",
                schema: "assignment_review",
                table: "ai_model_settings");

            migrationBuilder.DropColumn(
                name: "triage_model",
                schema: "assignment_review",
                table: "ai_model_settings");

            migrationBuilder.DropColumn(
                name: "triage_temperature",
                schema: "assignment_review",
                table: "ai_model_settings");

            migrationBuilder.DropColumn(
                name: "triage_timeout_seconds",
                schema: "assignment_review",
                table: "ai_model_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reference_links",
                schema: "assignment_review",
                table: "issue_review_specs",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "triage_max_output_tokens",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "triage_model",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "triage_temperature",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "triage_timeout_seconds",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "integer",
                nullable: true);
        }
    }
}
