using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RetireCertificatesAndMaterialNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "course_certificates",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "material_notes",
                schema: "progress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreates the retired schema only; removed rows require a verified backup to restore.
            migrationBuilder.CreateTable(
                name: "course_certificates",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    holder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_certificates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "material_notes",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_notes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_course_certificates_serial_number",
                schema: "progress",
                table: "course_certificates",
                column: "serial_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_course_certificates_user_id_course_id",
                schema: "progress",
                table: "course_certificates",
                columns: new[] { "user_id", "course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_material_notes_user_id_material_id",
                schema: "progress",
                table: "material_notes",
                columns: new[] { "user_id", "material_id" },
                unique: true);
        }
    }
}