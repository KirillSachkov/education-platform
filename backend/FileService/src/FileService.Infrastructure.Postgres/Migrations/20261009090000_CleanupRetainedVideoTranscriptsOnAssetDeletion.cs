using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FileService.Infrastructure.Postgres.Migrations;

[DbContext(typeof(FileServiceDbContext))]
[Migration("20261009090000_CleanupRetainedVideoTranscriptsOnAssetDeletion")]
public sealed class CleanupRetainedVideoTranscriptsOnAssetDeletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Every deletion path persists the same asset state. Keep all-version cleanup
        // in that database transaction, including replacement, retention and tombstone purge.
        migrationBuilder.Sql("""
            CREATE FUNCTION files.cleanup_retained_video_transcripts()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $cleanup$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    IF OLD.kind = 'VIDEO' THEN
                        DELETE FROM files.video_transcripts WHERE video_asset_id = OLD.id;
                    END IF;
                    RETURN OLD;
                END IF;

                IF NEW.kind = 'VIDEO' AND NEW.status IN ('DELETING', 'DELETED') THEN
                    DELETE FROM files.video_transcripts WHERE video_asset_id = NEW.id;
                END IF;
                RETURN NEW;
            END
            $cleanup$;

            CREATE TRIGGER retained_video_transcripts_cleanup
            AFTER UPDATE OF status OR DELETE ON files.media_assets
            FOR EACH ROW EXECUTE FUNCTION files.cleanup_retained_video_transcripts();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER retained_video_transcripts_cleanup ON files.media_assets;
            DROP FUNCTION files.cleanup_retained_video_transcripts();
            """);
    }
}