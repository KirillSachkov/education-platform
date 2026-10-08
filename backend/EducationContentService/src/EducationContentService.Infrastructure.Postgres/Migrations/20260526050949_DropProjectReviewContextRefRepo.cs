using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropProjectReviewContextRefRepo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ref_repo_branch",
                schema: "education",
                table: "project_review_contexts");

            migrationBuilder.DropColumn(
                name: "ref_repo_last_indexed_at",
                schema: "education",
                table: "project_review_contexts");

            migrationBuilder.DropColumn(
                name: "ref_repo_last_indexed_sha",
                schema: "education",
                table: "project_review_contexts");

            migrationBuilder.DropColumn(
                name: "ref_repo_url",
                schema: "education",
                table: "project_review_contexts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ref_repo_branch",
                schema: "education",
                table: "project_review_contexts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ref_repo_last_indexed_at",
                schema: "education",
                table: "project_review_contexts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ref_repo_last_indexed_sha",
                schema: "education",
                table: "project_review_contexts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ref_repo_url",
                schema: "education",
                table: "project_review_contexts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}
