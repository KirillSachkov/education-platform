using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAiReviewDenormFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ai_iterations_count",
                schema: "progress",
                table: "issue_submissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ai_review_status",
                schema: "progress",
                table: "issue_submissions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_ai_iteration_at",
                schema: "progress",
                table: "issue_submissions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "latest_ai_verdict",
                schema: "progress",
                table: "issue_submissions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ready_for_human_review",
                schema: "progress",
                table: "issue_submissions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_issue_submissions_ready_for_review_submitted_at",
                schema: "progress",
                table: "issue_submissions",
                columns: new[] { "review_status", "ready_for_human_review", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_issue_submissions_ready_for_review_submitted_at",
                schema: "progress",
                table: "issue_submissions");

            migrationBuilder.DropColumn(
                name: "ai_iterations_count",
                schema: "progress",
                table: "issue_submissions");

            migrationBuilder.DropColumn(
                name: "ai_review_status",
                schema: "progress",
                table: "issue_submissions");

            migrationBuilder.DropColumn(
                name: "last_ai_iteration_at",
                schema: "progress",
                table: "issue_submissions");

            migrationBuilder.DropColumn(
                name: "latest_ai_verdict",
                schema: "progress",
                table: "issue_submissions");

            migrationBuilder.DropColumn(
                name: "ready_for_human_review",
                schema: "progress",
                table: "issue_submissions");
        }
    }
}
