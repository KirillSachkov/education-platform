using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingSessionRevealPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOT NULL with a temporary END_OF_SESSION server default so the ADD COLUMN backfills any
            // pre-existing session rows with the correct enum member (not ""). Drop the default right
            // after — new sessions always supply a real RevealPolicy from the domain factory, and a
            // lingering DEFAULT risks EF silently omitting the column on future inserts (enum-casing
            // gotcha #1; same pattern as AddSessionTrackId).
            migrationBuilder.AddColumn<string>(
                name: "reveal_policy",
                schema: "trainer",
                table: "training_sessions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "END_OF_SESSION");

            migrationBuilder.Sql(
                "ALTER TABLE trainer.training_sessions ALTER COLUMN reveal_policy DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reveal_policy",
                schema: "trainer",
                table: "training_sessions");
        }
    }
}
