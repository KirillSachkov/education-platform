using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ExtendQuizForLevelTest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "level_test_config",
                schema: "education",
                table: "quizzes",
                type: "jsonb",
                nullable: true);

            // Backfill: все существующие квизы получают purpose='MATERIAL_CHECK'.
            // Новые квизы кладёт EF Core из доменного ctor'а (default = MATERIAL_CHECK),
            // DB-level default — backfill + страховка для raw-SQL INSERT'ов
            // (паттерн Course.Kind, миграция AddCourseKind).
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "education",
                table: "quizzes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "MATERIAL_CHECK");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "level_test_config",
                schema: "education",
                table: "quizzes");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "education",
                table: "quizzes");
        }
    }
}
