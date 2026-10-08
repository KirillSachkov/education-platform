using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddBookmarkTopicId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "topic_id",
                schema: "trainer",
                table: "bookmarked_questions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE trainer.bookmarked_questions AS bookmark
                SET topic_id = source.topic_id
                FROM (
                    SELECT DISTINCT ON (quiz_id) quiz_id, topic_id
                    FROM trainer.topic_banks
                    ORDER BY quiz_id,
                        CASE WHEN purpose = 'STUDY' THEN 0 ELSE 1 END,
                        sort_key,
                        created_at
                ) AS source
                WHERE bookmark.quiz_id = source.quiz_id
                  AND bookmark.topic_id IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_bookmarked_questions_user_topic",
                schema: "trainer",
                table: "bookmarked_questions",
                columns: new[] { "user_id", "topic_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookmarked_questions_user_topic",
                schema: "trainer",
                table: "bookmarked_questions");

            migrationBuilder.DropColumn(
                name: "topic_id",
                schema: "trainer",
                table: "bookmarked_questions");
        }
    }
}