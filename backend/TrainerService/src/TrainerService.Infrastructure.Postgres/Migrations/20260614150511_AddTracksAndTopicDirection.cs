using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTracksAndTopicDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direction",
                schema: "trainer",
                table: "topics",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            // NOT NULL with a temporary all-zeros server default so the ADD COLUMN backfills
            // the 2 pre-existing (throwaway) demo rows without error. Drop the default right
            // after — new topics always supply a real TrackId from the domain factory, and a
            // lingering DEFAULT risks EF silently omitting the column on future inserts.
            migrationBuilder.AddColumn<Guid>(
                name: "track_id",
                schema: "trainer",
                table: "topics",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.Sql(
                "ALTER TABLE trainer.topics ALTER COLUMN track_id DROP DEFAULT;");

            migrationBuilder.CreateTable(
                name: "tracks",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stack = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sort_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tracks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_topics_track_id",
                schema: "trainer",
                table: "topics",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ux_tracks_slug",
                schema: "trainer",
                table: "tracks",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tracks",
                schema: "trainer");

            migrationBuilder.DropIndex(
                name: "ix_topics_track_id",
                schema: "trainer",
                table: "topics");

            migrationBuilder.DropColumn(
                name: "direction",
                schema: "trainer",
                table: "topics");

            migrationBuilder.DropColumn(
                name: "track_id",
                schema: "trainer",
                table: "topics");
        }
    }
}
