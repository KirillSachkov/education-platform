using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    ///     Инверсия связи Quiz↔Material (#489): квиз становится standalone-сущностью,
    ///     материал ссылается на него через materials.quiz_id. Порядок шагов важен —
    ///     backfill читает quizzes.material_id, поэтому колонка дропается ПОСЛЕДНЕЙ.
    /// </remarks>
    public partial class InvertQuizMaterialLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Собственный уровень доступа квиза. DB-default 'PUBLIC' — только backfill
            //    существующих строк + страховка для raw-SQL INSERT'ов, как делал
            //    ExtendQuizForLevelTest c purpose. Новые квизы пишет EF из domain factory.
            migrationBuilder.AddColumn<string>(
                name: "access_type",
                schema: "education",
                table: "quizzes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "PUBLIC");

            // 2. Ссылка материала на квиз. Без FK — консистентно с video_id/image_id,
            //    обнуление при удалении квиза — cascade-SQL в DeleteQuizHandler.
            migrationBuilder.AddColumn<Guid>(
                name: "quiz_id",
                schema: "education",
                table: "materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_materials_quiz_id",
                schema: "education",
                table: "materials",
                column: "quiz_id");

            // 3. Привязки квизов к курсам — полное зеркало course_materials.
            migrationBuilder.CreateTable(
                name: "course_quizzes",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quiz_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_quizzes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_course_quizzes_course_id_quiz_id",
                schema: "education",
                table: "course_quizzes",
                columns: new[] { "course_id", "quiz_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_course_quizzes_course_id_sort_key",
                schema: "education",
                table: "course_quizzes",
                columns: new[] { "course_id", "sort_key" });

            // 4. Backfill (идемпотентный стиль): материал получает ссылку на свой квиз
            //    из старой связи quizzes.material_id (one-to-one — unique-индекс гарантировал).
            migrationBuilder.Sql("""
                UPDATE materials m
                SET quiz_id = q.id
                FROM quizzes q
                WHERE q.material_id = m.id
                  AND m.quiz_id IS DISTINCT FROM q.id;
                """);

            //    Привязки к курсам наследуются от привязок материала из course_materials,
            //    sort_key копируется. Дедуп через NOT EXISTS — повторный прогон no-op.
            migrationBuilder.Sql("""
                INSERT INTO course_quizzes (id, course_id, quiz_id, sort_key)
                SELECT gen_random_uuid(), cm.course_id, q.id, cm.sort_key
                FROM quizzes q
                JOIN course_materials cm ON cm.material_id = q.material_id
                WHERE q.material_id IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1
                      FROM course_quizzes cq
                      WHERE cq.course_id = cm.course_id
                        AND cq.quiz_id = q.id
                  );
                """);

            //    Bound-квизы наследуют уровень доступа своего материала; standalone
            //    (level-test) остаются PUBLIC — воронка теста уровня публична.
            migrationBuilder.Sql("""
                UPDATE quizzes q
                SET access_type = m.access_type
                FROM materials m
                WHERE q.material_id = m.id
                  AND q.access_type IS DISTINCT FROM m.access_type;
                """);

            // 5. Старая связь больше не нужна — дропаем ПОСЛЕДНИМ шагом (после backfill'а).
            migrationBuilder.DropForeignKey(
                name: "FK_quizzes_materials_material_id",
                schema: "education",
                table: "quizzes");

            migrationBuilder.DropIndex(
                name: "ix_quizzes_material_id",
                schema: "education",
                table: "quizzes");

            migrationBuilder.DropColumn(
                name: "material_id",
                schema: "education",
                table: "quizzes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "material_id",
                schema: "education",
                table: "quizzes",
                type: "uuid",
                nullable: true);

            // Best-effort обратный backfill: один материал на квиз (минимальный id) —
            // unique-индекс старой модели допускал ровно одну привязку.
            migrationBuilder.Sql("""
                UPDATE quizzes q
                SET material_id = picked.material_id
                FROM (
                    SELECT quiz_id, MIN(id) AS material_id
                    FROM materials
                    WHERE quiz_id IS NOT NULL
                    GROUP BY quiz_id
                ) picked
                WHERE picked.quiz_id = q.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_quizzes_material_id",
                schema: "education",
                table: "quizzes",
                column: "material_id",
                unique: true,
                filter: "material_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_quizzes_materials_material_id",
                schema: "education",
                table: "quizzes",
                column: "material_id",
                principalSchema: "education",
                principalTable: "materials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropTable(
                name: "course_quizzes",
                schema: "education");

            migrationBuilder.DropIndex(
                name: "ix_materials_quiz_id",
                schema: "education",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "quiz_id",
                schema: "education",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "access_type",
                schema: "education",
                table: "quizzes");
        }
    }
}
