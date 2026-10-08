using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialViewIsCompleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "completed_at",
                schema: "progress",
                table: "material_views",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_completed",
                schema: "progress",
                table: "material_views",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: до issue #285 любая запись в material_views означала «изучено» —
            // явный mark через UI-кнопку с cascade на module_item_progress и XP. Сохраняем
            // эту семантику: existing rows получают is_completed=TRUE и completed_at=viewed_at.
            // Новые silent track-view'ы будут вставляться с is_completed=FALSE.
            migrationBuilder.Sql("""
                UPDATE progress.material_views
                SET is_completed = TRUE,
                    completed_at = viewed_at
                WHERE is_completed = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completed_at",
                schema: "progress",
                table: "material_views");

            migrationBuilder.DropColumn(
                name: "is_completed",
                schema: "progress",
                table: "material_views");
        }
    }
}
