using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaterialProcessingService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddJobModelOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "model_override",
                schema: "material_processing",
                table: "timecode_generation_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "model_override",
                schema: "material_processing",
                table: "timecode_generation_jobs");
        }
    }
}
