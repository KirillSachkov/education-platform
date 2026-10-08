using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    ///     Generic-элементы подборок (#491): collection_items.material_id →
    ///     item_type ('MATERIAL' | 'QUIZ') + reference_id — зеркало module_items.
    ///     Намеренно НОВАЯ колонка + backfill + drop старой (не RenameColumn): порядок
    ///     шагов явный, backfill читает material_id, поэтому она дропается ПОСЛЕДНЕЙ.
    ///     FK на materials снят — generic-ссылка не может иметь FK на две таблицы;
    ///     существование валидирует AddItemHandler, каскады — Delete-use-case'ы.
    /// </remarks>
    public partial class GenericizeCollectionItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Тип элемента. DB-default 'MATERIAL' — backfill существующих строк
            //    (все они материальные) + страховка для raw-SQL INSERT'ов, как делал
            //    InvertQuizMaterialLink с access_type. Новые строки пишет EF из domain ctor.
            migrationBuilder.AddColumn<string>(
                name: "item_type",
                schema: "education",
                table: "collection_items",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "MATERIAL");

            // 2. Generic-ссылка: сперва nullable, затем backfill из material_id, затем NOT NULL.
            migrationBuilder.AddColumn<Guid>(
                name: "reference_id",
                schema: "education",
                table: "collection_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE collection_items
                SET reference_id = material_id
                WHERE reference_id IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "reference_id",
                schema: "education",
                table: "collection_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 3. Старые FK/индексы по material_id и сама колонка — после backfill'а.
            migrationBuilder.DropForeignKey(
                name: "FK_collection_items_materials_material_id",
                schema: "education",
                table: "collection_items");

            migrationBuilder.DropIndex(
                name: "IX_collection_items_material_id",
                schema: "education",
                table: "collection_items");

            migrationBuilder.DropIndex(
                name: "ux_collection_items_section_material",
                schema: "education",
                table: "collection_items");

            migrationBuilder.DropColumn(
                name: "material_id",
                schema: "education",
                table: "collection_items");

            // 4. Уникальность generic-ссылки в секции — наследник ux_collection_items_section_material.
            migrationBuilder.CreateIndex(
                name: "ux_collection_items_section_reference",
                schema: "education",
                table: "collection_items",
                columns: new[] { "section_id", "item_type", "reference_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_collection_items_section_reference",
                schema: "education",
                table: "collection_items");

            migrationBuilder.AddColumn<Guid>(
                name: "material_id",
                schema: "education",
                table: "collection_items",
                type: "uuid",
                nullable: true);

            // Best-effort обратный backfill: QUIZ-строки непредставимы в старой модели — дропаем.
            migrationBuilder.Sql("""
                DELETE FROM collection_items
                WHERE item_type <> 'MATERIAL';

                UPDATE collection_items
                SET material_id = reference_id
                WHERE material_id IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "material_id",
                schema: "education",
                table: "collection_items",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "item_type",
                schema: "education",
                table: "collection_items");

            migrationBuilder.DropColumn(
                name: "reference_id",
                schema: "education",
                table: "collection_items");

            migrationBuilder.CreateIndex(
                name: "IX_collection_items_material_id",
                schema: "education",
                table: "collection_items",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_collection_items_section_material",
                schema: "education",
                table: "collection_items",
                columns: new[] { "section_id", "material_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_collection_items_materials_material_id",
                schema: "education",
                table: "collection_items",
                column: "material_id",
                principalSchema: "education",
                principalTable: "materials",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
