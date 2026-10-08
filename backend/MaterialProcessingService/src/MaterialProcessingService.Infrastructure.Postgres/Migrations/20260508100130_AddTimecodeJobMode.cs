using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaterialProcessingService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTimecodeJobMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "mode",
                schema: "material_processing",
                table: "timecode_generation_jobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "TIMECODES");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mode",
                schema: "material_processing",
                table: "timecode_generation_jobs");
        }
    }
}
