using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MaterialProcessingService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAiModelSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_model_settings",
                schema: "material_processing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stt_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    stt_temperature = table.Column<double>(type: "double precision", nullable: true),
                    stt_max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    stt_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    timecode_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    timecode_temperature = table.Column<double>(type: "double precision", nullable: true),
                    timecode_max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    timecode_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    content_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_temperature = table.Column<double>(type: "double precision", nullable: true),
                    content_max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    content_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_model_settings", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_model_settings",
                schema: "material_processing");
        }
    }
}
