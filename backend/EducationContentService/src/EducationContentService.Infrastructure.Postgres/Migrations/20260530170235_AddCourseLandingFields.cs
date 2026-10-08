using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseLandingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "learning_outcomes",
                schema: "education",
                table: "courses",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "prerequisites",
                schema: "education",
                table: "courses",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string[]>(
                name: "target_audience",
                schema: "education",
                table: "courses",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "learning_outcomes",
                schema: "education",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "prerequisites",
                schema: "education",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "target_audience",
                schema: "education",
                table: "courses");
        }
    }
}
