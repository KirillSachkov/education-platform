using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseIsCatalogListed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true → existing PUBLISHED courses stay visible in the catalog
            // after the gate ships (#569). New non-admin courses get listed=false from the
            // domain ctor (EF writes the column explicitly — see CourseConfiguration), so the
            // DB-level default only governs the one-time backfill of pre-existing rows.
            migrationBuilder.AddColumn<bool>(
                name: "is_catalog_listed",
                schema: "education",
                table: "courses",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_catalog_listed",
                schema: "education",
                table: "courses");
        }
    }
}
