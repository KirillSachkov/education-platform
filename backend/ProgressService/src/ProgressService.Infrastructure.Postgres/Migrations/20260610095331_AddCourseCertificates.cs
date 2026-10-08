using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseCertificates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "course_certificates",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serial_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    course_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    holder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_certificates", x => x.id);
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "course_certificates",
                schema: "progress");
        }
    }
}
