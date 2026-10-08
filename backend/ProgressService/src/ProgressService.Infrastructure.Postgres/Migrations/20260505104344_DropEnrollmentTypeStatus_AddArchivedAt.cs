using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropEnrollmentTypeStatus_AddArchivedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "status",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.RenameColumn(
                name: "suspended_at",
                schema: "progress",
                table: "course_enrollments",
                newName: "archived_at");

            migrationBuilder.AddColumn<string>(
                name: "archive_reason",
                schema: "progress",
                table: "course_enrollments",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "archive_reason",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.RenameColumn(
                name: "archived_at",
                schema: "progress",
                table: "course_enrollments",
                newName: "suspended_at");

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "progress",
                table: "course_enrollments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "type",
                schema: "progress",
                table: "course_enrollments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");
        }
    }
}
