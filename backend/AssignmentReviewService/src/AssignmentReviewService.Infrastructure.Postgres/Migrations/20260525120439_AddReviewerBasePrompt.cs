using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewerBasePrompt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reviewer_base_prompt",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reviewer_base_prompt",
                schema: "assignment_review",
                table: "ai_model_settings");
        }
    }
}
