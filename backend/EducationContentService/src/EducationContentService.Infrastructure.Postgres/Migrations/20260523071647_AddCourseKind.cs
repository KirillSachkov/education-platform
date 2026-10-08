using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: все существующие курсы получают kind='COURSE'.
            // Новые курсы кладёт EF Core из доменного ctor'а (default = COURSE),
            // но колонке всё равно нужен DB-level default — иначе bulk-INSERT
            // через raw SQL (например, при seed) упрётся в NOT NULL без значения.
            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "education",
                table: "courses",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "COURSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                schema: "education",
                table: "courses");
        }
    }
}
