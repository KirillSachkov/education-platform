using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRepoContextLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "context_rounds",
                schema: "assignment_review",
                table: "ai_review_iterations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "requested_files",
                schema: "assignment_review",
                table: "ai_review_iterations",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "repo_context_enabled",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "context_rounds",
                schema: "assignment_review",
                table: "ai_review_iterations");

            migrationBuilder.DropColumn(
                name: "requested_files",
                schema: "assignment_review",
                table: "ai_review_iterations");

            migrationBuilder.DropColumn(
                name: "repo_context_enabled",
                schema: "assignment_review",
                table: "ai_model_settings");
        }
    }
}
