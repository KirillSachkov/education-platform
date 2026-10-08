using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ExtendQuizForEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "material_id",
                schema: "education",
                table: "quizzes",
                type: "uuid",
                nullable: true);

            // defaultValue 70 (не EF-шный 0): спецификация ST-H (#472) — "passing_score_percent
            // int NOT NULL default 70". В модели дефолта нет (значение всегда ставит
            // конструктор агрегата) — DB DEFAULT работает только как backfill/страховка.
            migrationBuilder.AddColumn<int>(
                name: "passing_score_percent",
                schema: "education",
                table: "quizzes",
                type: "integer",
                nullable: false,
                defaultValue: 70);

            migrationBuilder.AddColumn<string>(
                name: "questions",
                schema: "education",
                table: "quizzes",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.DropColumn(
                name: "passing_score_percent",
                schema: "education",
                table: "quizzes");

            migrationBuilder.DropColumn(
                name: "questions",
                schema: "education",
                table: "quizzes");
        }
    }
}
