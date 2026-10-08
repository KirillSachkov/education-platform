using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaterialProcessingService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddContentJobModelOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Корректирующая миграция: предыдущая `20260508112734_AddJobModelOverride`
            // ошибочно добавила `model_override` только в `timecode_generation_jobs`,
            // хотя `ContentGenerationJob` тоже мапит это поле. Идемпотентная — чтобы
            // dev-окружения, где колонка уже была добавлена вручную через ALTER TABLE,
            // не падали с 42701 (duplicate column) при следующем migrate.
            migrationBuilder.Sql(
                "ALTER TABLE material_processing.content_generation_jobs " +
                "ADD COLUMN IF NOT EXISTS model_override character varying(100);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE material_processing.content_generation_jobs " +
                "DROP COLUMN IF EXISTS model_override;");
        }
    }
}
