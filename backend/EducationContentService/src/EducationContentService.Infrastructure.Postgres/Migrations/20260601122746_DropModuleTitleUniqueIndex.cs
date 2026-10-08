using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropModuleTitleUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_modules_title",
                schema: "education",
                table: "modules");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_modules_title",
                schema: "education",
                table: "modules",
                column: "title",
                unique: true,
                filter: "status <> 'DRAFT'");
        }
    }
}
