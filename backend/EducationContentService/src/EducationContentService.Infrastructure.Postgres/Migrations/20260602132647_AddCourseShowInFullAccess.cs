using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseShowInFullAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: все существующие курсы получают show_in_full_access=true (показываем в
            // витрине «Полный доступ» по умолчанию). Новые курсы пишет EF Core из доменного ctor'а
            // (default = true); DB-level default нужен только для backfill'а и raw-SQL bulk-INSERT'ов.
            migrationBuilder.AddColumn<bool>(
                name: "show_in_full_access",
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
                name: "show_in_full_access",
                schema: "education",
                table: "courses");
        }
    }
}
