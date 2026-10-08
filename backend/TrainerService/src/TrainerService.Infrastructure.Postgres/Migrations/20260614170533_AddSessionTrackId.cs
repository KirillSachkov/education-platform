using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionTrackId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOT NULL with a temporary all-zeros server default so the ADD COLUMN backfills any
            // pre-existing (seeded) session rows without error. Drop the default right after — new
            // sessions always supply a real TrackId from the domain factory, and a lingering DEFAULT
            // risks EF silently omitting the column on future inserts (enum-casing gotcha #1).
            migrationBuilder.AddColumn<Guid>(
                name: "track_id",
                schema: "trainer",
                table: "training_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.Sql(
                "ALTER TABLE trainer.training_sessions ALTER COLUMN track_id DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_training_sessions_user_id_started_at",
                schema: "trainer",
                table: "training_sessions",
                columns: new[] { "user_id", "started_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_training_sessions_user_id_started_at",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "track_id",
                schema: "trainer",
                table: "training_sessions");
        }
    }
}
