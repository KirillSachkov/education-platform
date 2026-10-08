using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainerOrderingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_topic_banks_topic_id",
                schema: "trainer",
                table: "topic_banks");

            migrationBuilder.DropIndex(
                name: "IX_trainer_questions_bank_id",
                schema: "trainer",
                table: "trainer_questions");

            migrationBuilder.CreateIndex(
                name: "ix_trainer_questions_bank_sort_key",
                schema: "trainer",
                table: "trainer_questions",
                columns: new[] { "bank_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_tracks_sort_key",
                schema: "trainer",
                table: "tracks",
                column: "sort_key");

            migrationBuilder.CreateIndex(
                name: "ix_topics_sort_key",
                schema: "trainer",
                table: "topics",
                column: "sort_key");

            migrationBuilder.CreateIndex(
                name: "ix_topic_banks_topic_sort_key",
                schema: "trainer",
                table: "topic_banks",
                columns: new[] { "topic_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_mock_interviews_sort_index",
                schema: "trainer",
                table: "mock_interviews",
                column: "sort_index");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_trainer_questions_bank_sort_key",
                schema: "trainer",
                table: "trainer_questions");

            migrationBuilder.DropIndex(
                name: "ix_tracks_sort_key",
                schema: "trainer",
                table: "tracks");

            migrationBuilder.DropIndex(
                name: "ix_topics_sort_key",
                schema: "trainer",
                table: "topics");

            migrationBuilder.DropIndex(
                name: "ix_topic_banks_topic_sort_key",
                schema: "trainer",
                table: "topic_banks");

            migrationBuilder.DropIndex(
                name: "ix_mock_interviews_sort_index",
                schema: "trainer",
                table: "mock_interviews");

            migrationBuilder.CreateIndex(
                name: "IX_trainer_questions_bank_id",
                schema: "trainer",
                table: "trainer_questions",
                column: "bank_id");

            migrationBuilder.CreateIndex(
                name: "ix_topic_banks_topic_id",
                schema: "trainer",
                table: "topic_banks",
                column: "topic_id");
        }
    }
}
