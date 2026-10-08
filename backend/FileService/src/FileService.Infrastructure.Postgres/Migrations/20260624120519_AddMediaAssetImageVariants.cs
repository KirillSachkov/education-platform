using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaAssetImageVariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // jsonb requires valid JSON — backfill existing rows with an empty array,
            // not "" (which is not valid jsonb). The whole-list converter on
            // MediaAsset._imageVariants writes "[]" for an empty set going forward.
            migrationBuilder.AddColumn<string>(
                name: "image_variants",
                schema: "files",
                table: "media_assets",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_variants",
                schema: "files",
                table: "media_assets");
        }
    }
}
