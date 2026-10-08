using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectReviewSettingsAndIssueSubmissionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requires_github_connection",
                schema: "education",
                table: "project_review_contexts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_review_app",
                schema: "education",
                table: "project_review_contexts",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "self_check_instructions",
                schema: "education",
                table: "issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "submission_mode",
                schema: "education",
                table: "issues",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE education.project_review_contexts
                SET requires_github_connection = TRUE,
                    requires_review_app = TRUE
                WHERE requires_github_connection IS NULL
                   OR requires_review_app IS NULL;
                """);

            migrationBuilder.Sql("""
                UPDATE education.issues
                SET submission_mode = 'PULL_REQUEST'
                WHERE submission_mode IS NULL;
                """);

            migrationBuilder.AlterColumn<bool>(
                name: "requires_github_connection",
                schema: "education",
                table: "project_review_contexts",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "requires_review_app",
                schema: "education",
                table: "project_review_contexts",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "submission_mode",
                schema: "education",
                table: "issues",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requires_github_connection",
                schema: "education",
                table: "project_review_contexts");

            migrationBuilder.DropColumn(
                name: "requires_review_app",
                schema: "education",
                table: "project_review_contexts");

            migrationBuilder.DropColumn(
                name: "self_check_instructions",
                schema: "education",
                table: "issues");

            migrationBuilder.DropColumn(
                name: "submission_mode",
                schema: "education",
                table: "issues");
        }
    }
}
