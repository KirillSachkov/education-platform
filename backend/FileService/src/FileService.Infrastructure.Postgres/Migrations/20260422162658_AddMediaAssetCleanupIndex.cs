using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <summary>
    ///     Partial index to accelerate media-asset cleanup batches
    ///     (PENDING_UPLOAD / PROCESSING rows or any row still attached to a draft).
    /// </summary>
    public partial class AddMediaAssetCleanupIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_media_assets_cleanup
                    ON files.media_assets (kind, status, created_at)
                    WHERE status IN ('PENDING_UPLOAD', 'PROCESSING') OR draft_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS files.ix_media_assets_cleanup;");
        }
    }
}
