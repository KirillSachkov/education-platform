using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewSpecsAndGuidelines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "issue_review_specs",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_prompt = table.Column<string>(type: "text", nullable: true),
                    review_aspects = table.Column<string>(type: "text", nullable: true),
                    reference_links = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_review_specs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "project_review_guidelines",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guidelines_markdown = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_review_guidelines", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_issue_review_specs_project",
                schema: "assignment_review",
                table: "issue_review_specs",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "uq_issue_review_specs_issue",
                schema: "assignment_review",
                table: "issue_review_specs",
                column: "issue_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_project_review_guidelines_project",
                schema: "assignment_review",
                table: "project_review_guidelines",
                column: "project_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "issue_review_specs",
                schema: "assignment_review");

            migrationBuilder.DropTable(
                name: "project_review_guidelines",
                schema: "assignment_review");
        }
    }
}
