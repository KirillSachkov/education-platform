using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveLevelTestQuizSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Preserve learning content while removing every reference to a retired quiz.
            migrationBuilder.Sql("""
                UPDATE education.materials SET quiz_id = NULL
                WHERE quiz_id IN (SELECT id FROM education.quizzes WHERE purpose = 'LEVEL_TEST');

                DELETE FROM education.course_quizzes
                WHERE quiz_id IN (SELECT id FROM education.quizzes WHERE purpose = 'LEVEL_TEST');

                DELETE FROM education.collection_items
                WHERE item_type = 'QUIZ'
                  AND reference_id IN (SELECT id FROM education.quizzes WHERE purpose = 'LEVEL_TEST');

                DELETE FROM education.module_items
                WHERE item_type = 'Quiz'
                  AND reference_id IN (SELECT id FROM education.quizzes WHERE purpose = 'LEVEL_TEST');

                DELETE FROM education.quizzes WHERE purpose = 'LEVEL_TEST';
                """);

            migrationBuilder.DropColumn(
                name: "level_test_config",
                schema: "education",
                table: "quizzes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "level_test_config",
                schema: "education",
                table: "quizzes",
                type: "jsonb",
                nullable: true);
        }
    }
}