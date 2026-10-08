using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileService.Infrastructure.Postgres.Migrations;

/// <inheritdoc />
/// <remarks>
///     Защита от race condition: одновременные BindAsset/CompleteFileUpload для одного
///     single-asset-per-entity слота (AVATAR, COURSE_PREVIEW, COURSE_VIDEO,
///     MATERIAL_PREVIEW, MATERIAL_VIDEO, COLLECTION_COVER) могли создать два READY-asset'а,
///     ссылающихся на один (target_entity, usage_type). Partial unique index
///     закрывает это окно гонки на уровне БД.
/// </remarks>
public partial class AddSingleAssetSlotUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS ix_media_assets_single_asset_slot
            ON files.media_assets (target_entity_type, target_entity_id, usage_type)
            WHERE status = 'READY'
              AND target_entity_id IS NOT NULL
              AND usage_type IN (
                  'AVATAR',
                  'COURSE_PREVIEW',
                  'COURSE_VIDEO',
                  'MATERIAL_PREVIEW',
                  'MATERIAL_VIDEO',
                  'COLLECTION_COVER'
              );
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS files.ix_media_assets_single_asset_slot;");
    }
}
