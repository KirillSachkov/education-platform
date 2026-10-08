using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectReviewContextAndSpec : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "project_review_contexts",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guidelines_markdown = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: false),
                    ref_repo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ref_repo_branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ref_repo_last_indexed_sha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ref_repo_last_indexed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_auto_review_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_review_contexts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "review_specs",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_prompt = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    review_aspects = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    reference_links = table.Column<string>(type: "jsonb", nullable: false),
                    is_auto_review_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_review_specs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_project_review_contexts_project",
                schema: "education",
                table: "project_review_contexts",
                column: "project_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_review_specs_project_id",
                schema: "education",
                table: "review_specs",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "uq_review_specs_issue",
                schema: "education",
                table: "review_specs",
                column: "issue_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_review_contexts",
                schema: "education");

            migrationBuilder.DropTable(
                name: "review_specs",
                schema: "education");
        }
    }
}
