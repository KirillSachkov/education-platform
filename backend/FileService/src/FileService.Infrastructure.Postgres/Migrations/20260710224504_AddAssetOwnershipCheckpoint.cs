using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FileService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAssetOwnershipCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_ownership_checkpoints",
                schema: "files",
                columns: table => new
                {
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desired_owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_applied_revision = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_asset_ownership_checkpoints", x => new { x.course_id, x.target_type, x.target_id });
                });

            migrationBuilder.CreateIndex(
                name: "IX_asset_ownership_checkpoints_target_type_target_id_last_appl~",
                schema: "files",
                table: "asset_ownership_checkpoints",
                columns: new[] { "target_type", "target_id", "last_applied_revision" },
                descending: new[] { false, false, true });

            migrationBuilder.Sql(
                """
                CREATE FUNCTION files.apply_asset_desired_owner()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    projected_owner uuid;
                BEGIN
                    IF NEW.target_entity_type IS NOT NULL AND NEW.target_entity_id IS NOT NULL THEN
                        PERFORM pg_advisory_xact_lock(hashtextextended(
                            NEW.target_entity_type || ':' || NEW.target_entity_id::text,
                            0));

                        SELECT checkpoint.desired_owner_id
                        INTO projected_owner
                        FROM files.asset_ownership_checkpoints checkpoint
                        WHERE checkpoint.target_type = NEW.target_entity_type
                          AND checkpoint.target_id = NEW.target_entity_id
                        ORDER BY checkpoint.last_applied_revision DESC
                        LIMIT 1;

                        IF FOUND THEN
                            NEW.uploaded_by_user_id := projected_owner;
                        END IF;
                    END IF;

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER media_assets_apply_desired_owner
                BEFORE INSERT OR UPDATE OF target_entity_type, target_entity_id
                ON files.media_assets
                FOR EACH ROW
                EXECUTE FUNCTION files.apply_asset_desired_owner();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS media_assets_apply_desired_owner ON files.media_assets;
                DROP FUNCTION IF EXISTS files.apply_asset_desired_owner();
                """);

            migrationBuilder.DropTable(
                name: "asset_ownership_checkpoints",
                schema: "files");
        }
    }
}
