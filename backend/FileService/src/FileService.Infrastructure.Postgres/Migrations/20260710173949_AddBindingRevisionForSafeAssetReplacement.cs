using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddBindingRevisionForSafeAssetReplacement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "asset_binding_revision_seq",
                schema: "files");

            migrationBuilder.AddColumn<long>(
                name: "binding_revision",
                schema: "files",
                table: "media_assets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "binding_selection_id",
                schema: "files",
                table: "media_assets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "confirmed_binding_revision",
                schema: "files",
                table: "media_assets",
                type: "bigint",
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.AddColumn<long>(
                name: "detached_through_binding_revision",
                schema: "files",
                table: "media_assets",
                type: "bigint",
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.Sql(
                """
                UPDATE files.media_assets
                SET confirmed_binding_revision = 0
                WHERE target_entity_id IS NOT NULL
                  AND status IN ('READY', 'PROCESSING')
                  AND (
                      (usage_type = 'AVATAR' AND EXISTS (
                          SELECT 1
                          FROM auth.user_profiles profile
                          WHERE profile.id = files.media_assets.target_entity_id
                            AND profile.avatar_id = files.media_assets.id
                      ))
                      OR (usage_type = 'MATERIAL_PREVIEW' AND EXISTS (
                          SELECT 1
                          FROM education.materials material
                          WHERE material.id = files.media_assets.target_entity_id
                            AND material.image_id = files.media_assets.id
                      ))
                      OR (usage_type = 'MATERIAL_VIDEO' AND EXISTS (
                          SELECT 1
                          FROM education.materials material
                          WHERE material.id = files.media_assets.target_entity_id
                            AND material.video_id = files.media_assets.id
                      ))
                      OR (usage_type = 'COURSE_PREVIEW' AND EXISTS (
                          SELECT 1
                          FROM education.courses course_row
                          WHERE course_row.id = files.media_assets.target_entity_id
                            AND course_row.image_id = files.media_assets.id
                      ))
                      OR (usage_type = 'COURSE_VIDEO' AND EXISTS (
                          SELECT 1
                          FROM education.courses course_row
                          WHERE course_row.id = files.media_assets.target_entity_id
                            AND course_row.video_id = files.media_assets.id
                      ))
                      OR (usage_type = 'COLLECTION_COVER' AND EXISTS (
                          SELECT 1
                          FROM education.collections collection_row
                          WHERE collection_row.id = files.media_assets.target_entity_id
                            AND collection_row.cover_image_id = files.media_assets.id
                      ))
                  );

                DROP INDEX IF EXISTS files.ix_media_assets_single_asset_slot;

                CREATE INDEX ix_media_assets_single_asset_slot
                ON files.media_assets (
                    target_entity_type,
                    target_entity_id,
                    usage_type,
                    confirmed_binding_revision DESC,
                    binding_revision DESC
                )
                WHERE status IN ('READY', 'PROCESSING')
                  AND target_entity_id IS NOT NULL
                  AND usage_type IN (
                      'AVATAR', 'COURSE_PREVIEW', 'COURSE_VIDEO',
                      'MATERIAL_PREVIEW', 'MATERIAL_VIDEO', 'COLLECTION_COVER'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Multiple READY rows are expected while the authoritative aggregate
            // confirms a replacement. Preserve the highest confirmed/logical revision
            // before restoring the legacy UNIQUE slot invariant.
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS files.ix_media_assets_single_asset_slot;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM files.media_assets
                        WHERE status NOT IN ('DELETING', 'DELETED')
                          AND target_entity_id IS NOT NULL
                          AND usage_type IN (
                              'AVATAR', 'COURSE_PREVIEW', 'COURSE_VIDEO',
                              'MATERIAL_PREVIEW', 'MATERIAL_VIDEO', 'COLLECTION_COVER'
                          )
                          AND binding_revision <> confirmed_binding_revision
                    ) THEN
                        RAISE EXCEPTION
                            'Unsafe FileService downgrade: stop media writes, drain ECS/Auth outboxes and file binding queues, then reconcile every prepared revision before retrying';
                    END IF;
                END $$;

                WITH ranked AS (
                    SELECT id,
                           row_number() OVER (
                               PARTITION BY target_entity_type, target_entity_id, usage_type
                               ORDER BY (confirmed_binding_revision > 0 OR binding_revision = 0) DESC,
                                        confirmed_binding_revision DESC,
                                        binding_revision DESC,
                                        bound_at DESC NULLS LAST,
                                        created_at DESC,
                                        id DESC
                           ) AS position
                    FROM files.media_assets
                    WHERE status = 'READY'
                      AND target_entity_id IS NOT NULL
                      AND usage_type IN (
                          'AVATAR', 'COURSE_PREVIEW', 'COURSE_VIDEO',
                          'MATERIAL_PREVIEW', 'MATERIAL_VIDEO', 'COLLECTION_COVER'
                      )
                )
                UPDATE files.media_assets AS asset
                SET status = 'DELETING',
                    delete_requested_at = COALESCE(asset.delete_requested_at, timezone('utc', now()))
                FROM ranked
                WHERE asset.id = ranked.id
                  AND ranked.position > 1;

                CREATE UNIQUE INDEX ix_media_assets_single_asset_slot
                ON files.media_assets (target_entity_type, target_entity_id, usage_type)
                WHERE status = 'READY'
                  AND target_entity_id IS NOT NULL
                  AND usage_type IN (
                      'AVATAR', 'COURSE_PREVIEW', 'COURSE_VIDEO',
                      'MATERIAL_PREVIEW', 'MATERIAL_VIDEO', 'COLLECTION_COVER'
                  );
                """);

            migrationBuilder.DropColumn(
                name: "binding_revision",
                schema: "files",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "binding_selection_id",
                schema: "files",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "confirmed_binding_revision",
                schema: "files",
                table: "media_assets");

            migrationBuilder.DropColumn(
                name: "detached_through_binding_revision",
                schema: "files",
                table: "media_assets");

            migrationBuilder.DropSequence(
                name: "asset_binding_revision_seq",
                schema: "files");
        }
    }
}
