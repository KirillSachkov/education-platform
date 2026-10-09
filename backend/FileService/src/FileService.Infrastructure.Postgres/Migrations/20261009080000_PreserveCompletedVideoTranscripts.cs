using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FileService.Infrastructure.Postgres.Migrations;

[DbContext(typeof(FileServiceDbContext))]
[Migration("20261009080000_PreserveCompletedVideoTranscripts")]
public sealed class PreserveCompletedVideoTranscripts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // This retained-artifact table is read through Dapper, not an EF aggregate.
        // Copy the raw JSON and metadata without reserializing or filtering segments.
        migrationBuilder.Sql(
            """
            CREATE TABLE files.video_transcripts (
                id uuid PRIMARY KEY,
                video_asset_id uuid NOT NULL,
                asset_version uuid NOT NULL,
                duration_seconds integer NOT NULL,
                language varchar(16) NOT NULL,
                segments_json jsonb NOT NULL,
                created_at timestamptz NOT NULL,
                updated_at timestamptz NOT NULL,
                UNIQUE (video_asset_id, asset_version)
            );

            DO $copy$
            BEGIN
                IF to_regclass('material_processing.video_transcripts') IS NOT NULL THEN
                    INSERT INTO files.video_transcripts
                        (id, video_asset_id, asset_version, duration_seconds, language,
                         segments_json, created_at, updated_at)
                    SELECT id, video_asset_id, asset_version, duration_seconds, language,
                           segments_json, created_at, updated_at
                    FROM material_processing.video_transcripts;
                END IF;
            END
            $copy$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // A rollback must not erase the only active copy of stored transcripts.
        migrationBuilder.Sql(
            """
            DO $guard$
            BEGIN
                RAISE EXCEPTION 'Retained video transcripts require an explicit backup and restore plan; downgrade is blocked.';
            END
            $guard$;
            """);
    }
}