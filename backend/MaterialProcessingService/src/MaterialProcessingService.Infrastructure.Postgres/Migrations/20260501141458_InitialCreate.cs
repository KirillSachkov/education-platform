using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaterialProcessingService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "material_processing");

            migrationBuilder.CreateTable(
                name: "content_generation_jobs",
                schema: "material_processing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_version = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    progress_percent = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_generation_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "timecode_generation_jobs",
                schema: "material_processing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_version = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    progress_percent = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    error_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_timecode_generation_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "video_transcripts",
                schema: "material_processing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    video_asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_version = table.Column<Guid>(type: "uuid", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    segments_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_transcripts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_content_jobs_video_material_status",
                schema: "material_processing",
                table: "content_generation_jobs",
                columns: new[] { "video_asset_id", "material_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_content_jobs_video_material_active",
                schema: "material_processing",
                table: "content_generation_jobs",
                columns: new[] { "video_asset_id", "material_id" },
                unique: true,
                filter: "status IN ('QUEUED', 'PROCESSING')");

            migrationBuilder.CreateIndex(
                name: "ix_timecode_jobs_video_status",
                schema: "material_processing",
                table: "timecode_generation_jobs",
                columns: new[] { "video_asset_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_timecode_jobs_video_active",
                schema: "material_processing",
                table: "timecode_generation_jobs",
                column: "video_asset_id",
                unique: true,
                filter: "status IN ('QUEUED', 'PROCESSING')");

            migrationBuilder.CreateIndex(
                name: "ix_video_transcripts_video_asset_version",
                schema: "material_processing",
                table: "video_transcripts",
                columns: new[] { "video_asset_id", "asset_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "content_generation_jobs",
                schema: "material_processing");

            migrationBuilder.DropTable(
                name: "timecode_generation_jobs",
                schema: "material_processing");

            migrationBuilder.DropTable(
                name: "video_transcripts",
                schema: "material_processing");
        }
    }
}
