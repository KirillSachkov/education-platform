using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaterialProcessingService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddStuckJobHeartbeatIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_timecode_jobs_status_updated_at",
                schema: "material_processing",
                table: "timecode_generation_jobs",
                columns: new[] { "status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_jobs_status_updated_at",
                schema: "material_processing",
                table: "content_generation_jobs",
                columns: new[] { "status", "updated_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_timecode_jobs_status_updated_at",
                schema: "material_processing",
                table: "timecode_generation_jobs");

            migrationBuilder.DropIndex(
                name: "ix_content_jobs_status_updated_at",
                schema: "material_processing",
                table: "content_generation_jobs");
        }
    }
}
