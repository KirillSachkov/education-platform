using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaterialProcessingService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoVideoProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "material_id",
                schema: "material_processing",
                table: "timecode_generation_jobs",
                type: "uuid",
                nullable: true);

            // defaultValue 'MANUAL' бэкфиллит существующие job'ы (все они были запущены
            // вручную). EF-config без HasDefaultValue → новые строки всегда несут значение
            // из домена; DB-DEFAULT остаётся только для backfill (issue #648).
            migrationBuilder.AddColumn<string>(
                name: "trigger_source",
                schema: "material_processing",
                table: "timecode_generation_jobs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "MANUAL");

            // Авто-обработка включена по умолчанию — бэкфиллим существующую singleton-строку
            // настроек значением true (issue #648).
            migrationBuilder.AddColumn<bool>(
                name: "auto_process_videos_enabled",
                schema: "material_processing",
                table: "ai_model_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "material_id",
                schema: "material_processing",
                table: "timecode_generation_jobs");

            migrationBuilder.DropColumn(
                name: "trigger_source",
                schema: "material_processing",
                table: "timecode_generation_jobs");

            migrationBuilder.DropColumn(
                name: "auto_process_videos_enabled",
                schema: "material_processing",
                table: "ai_model_settings");
        }
    }
}
