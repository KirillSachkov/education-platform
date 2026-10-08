using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMockSessionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "time_limit_seconds",
                schema: "trainer",
                table: "training_sessions",
                type: "integer",
                nullable: true);

            // NOT NULL with a temporary all-zeros server default so the ADD COLUMN backfills any
            // pre-existing (throwaway dev) item rows without error. Drop the default right after —
            // new items always supply a real TopicId from the domain factory, and a lingering
            // DEFAULT risks EF silently omitting the column on future inserts (enum-casing gotcha #1).
            migrationBuilder.AddColumn<Guid>(
                name: "topic_id",
                schema: "trainer",
                table: "training_session_items",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.Sql(
                "ALTER TABLE trainer.training_session_items ALTER COLUMN topic_id DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "time_limit_seconds",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropColumn(
                name: "topic_id",
                schema: "trainer",
                table: "training_session_items");
        }
    }
}
