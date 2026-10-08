using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropEnrollmentArchiveSortKeySourceRef : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_course_enrollments_user_id_sort_key",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.DropColumn(
                name: "archive_reason",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.DropColumn(
                name: "archived_at",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.DropColumn(
                name: "sort_key",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.DropColumn(
                name: "source_ref",
                schema: "progress",
                table: "course_enrollments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "archive_reason",
                schema: "progress",
                table: "course_enrollments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "archived_at",
                schema: "progress",
                table: "course_enrollments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sort_key",
                schema: "progress",
                table: "course_enrollments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                collation: "C");

            migrationBuilder.AddColumn<string>(
                name: "source_ref",
                schema: "progress",
                table: "course_enrollments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_course_enrollments_user_id_sort_key",
                schema: "progress",
                table: "course_enrollments",
                columns: new[] { "user_id", "sort_key" });
        }
    }
}
