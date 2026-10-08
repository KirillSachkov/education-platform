using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "files");

            migrationBuilder.CreateTable(
                name: "media_assets",
                schema: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    usage_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    target_entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_temporary = table.Column<bool>(type: "boolean", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    bound_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processing_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    delete_requested_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "file_storage_refs",
                schema: "files",
                columns: table => new
                {
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_file_storage_refs", x => x.asset_id);
                    table.ForeignKey(
                        name: "FK_file_storage_refs_media_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "files",
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_provider_refs",
                schema: "files",
                columns: table => new
                {
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_asset_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_provider_refs", x => x.asset_id);
                    table.ForeignKey(
                        name: "FK_video_provider_refs_media_assets_asset_id",
                        column: x => x.asset_id,
                        principalSchema: "files",
                        principalTable: "media_assets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_file_storage_refs_storage_key",
                schema: "files",
                table: "file_storage_refs",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_draft_id",
                schema: "files",
                table: "media_assets",
                column: "draft_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_status",
                schema: "files",
                table: "media_assets",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_target_entity_id_target_entity_type",
                schema: "files",
                table: "media_assets",
                columns: new[] { "target_entity_id", "target_entity_type" });

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_usage_type",
                schema: "files",
                table: "media_assets",
                column: "usage_type");

            migrationBuilder.CreateIndex(
                name: "IX_video_provider_refs_external_asset_id",
                schema: "files",
                table: "video_provider_refs",
                column: "external_asset_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_storage_refs",
                schema: "files");

            migrationBuilder.DropTable(
                name: "video_provider_refs",
                schema: "files");

            migrationBuilder.DropTable(
                name: "media_assets",
                schema: "files");
        }
    }
}
