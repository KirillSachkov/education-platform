using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentTargetOwnershipContractV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_module_items_reference_id_item_type_module_id",
                schema: "education",
                table: "module_items",
                columns: new[] { "reference_id", "item_type", "module_id" });

            migrationBuilder.CreateIndex(
                name: "IX_course_quizzes_quiz_id_course_id",
                schema: "education",
                table: "course_quizzes",
                columns: new[] { "quiz_id", "course_id" });

            migrationBuilder.CreateIndex(
                name: "IX_course_materials_material_id_course_id",
                schema: "education",
                table: "course_materials",
                columns: new[] { "material_id", "course_id" });

            migrationBuilder.CreateIndex(
                name: "IX_course_items_reference_id_item_type_course_id",
                schema: "education",
                table: "course_items",
                columns: new[] { "reference_id", "item_type", "course_id" });

            migrationBuilder.Sql("""
                CREATE VIEW education.comment_target_ownership_v1
                WITH (security_barrier = true)
                AS
                WITH ownership_candidates AS (
                    SELECT 'course'::text AS target_entity_type,
                        course.id AS target_entity_id,
                        course.author_id,
                        course.id AS course_id,
                        1 AS priority
                    FROM education.courses course

                    UNION ALL

                    SELECT 'material', material.id, course.author_id, binding.course_id, 1
                    FROM education.materials material
                    JOIN education.course_materials binding ON binding.material_id = material.id
                    JOIN education.courses course ON course.id = binding.course_id
                    UNION ALL
                    SELECT 'material', material.id, course.author_id, course_item.course_id, 2
                    FROM education.materials material
                    JOIN education.module_items module_item
                        ON module_item.item_type = 'Material'
                        AND module_item.reference_id = material.id
                    JOIN education.course_items course_item
                        ON course_item.item_type = 'Module'
                        AND course_item.reference_id = module_item.module_id
                    JOIN education.courses course ON course.id = course_item.course_id
                    UNION ALL
                    SELECT 'material', material.id, material.author_id, NULL::uuid, 3
                    FROM education.materials material

                    UNION ALL

                    SELECT 'issue', issue.id, course.author_id, course_item.course_id, 1
                    FROM education.issues issue
                    JOIN education.course_items course_item
                        ON course_item.item_type = 'Project'
                        AND course_item.reference_id = issue.project_id
                    JOIN education.courses course ON course.id = course_item.course_id
                    UNION ALL
                    SELECT 'issue', issue.id, course.author_id, course_item.course_id, 2
                    FROM education.issues issue
                    JOIN education.module_items module_item
                        ON module_item.item_type = 'Issue'
                        AND module_item.reference_id = issue.id
                    JOIN education.course_items course_item
                        ON course_item.item_type = 'Module'
                        AND course_item.reference_id = module_item.module_id
                    JOIN education.courses course ON course.id = course_item.course_id
                    UNION ALL
                    SELECT 'issue', issue.id, issue.author_id, NULL::uuid, 3
                    FROM education.issues issue

                    UNION ALL

                    SELECT 'quiz', quiz.id, course.author_id, binding.course_id, 1
                    FROM education.quizzes quiz
                    JOIN education.course_quizzes binding ON binding.quiz_id = quiz.id
                    JOIN education.courses course ON course.id = binding.course_id
                    UNION ALL
                    SELECT 'quiz', quiz.id, quiz.author_id, NULL::uuid, 2
                    FROM education.quizzes quiz
                )
                SELECT DISTINCT ON (target_entity_type, target_entity_id)
                    target_entity_type,
                    target_entity_id,
                    author_id
                FROM ownership_candidates
                ORDER BY target_entity_type, target_entity_id, priority, course_id NULLS LAST;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS education.comment_target_ownership_v1;");

            migrationBuilder.DropIndex(
                name: "IX_module_items_reference_id_item_type_module_id",
                schema: "education",
                table: "module_items");

            migrationBuilder.DropIndex(
                name: "IX_course_quizzes_quiz_id_course_id",
                schema: "education",
                table: "course_quizzes");

            migrationBuilder.DropIndex(
                name: "IX_course_materials_material_id_course_id",
                schema: "education",
                table: "course_materials");

            migrationBuilder.DropIndex(
                name: "IX_course_items_reference_id_item_type_course_id",
                schema: "education",
                table: "course_items");
        }
    }
}