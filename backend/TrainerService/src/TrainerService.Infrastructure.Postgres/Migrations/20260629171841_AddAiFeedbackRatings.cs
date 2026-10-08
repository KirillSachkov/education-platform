using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAiFeedbackRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_feedback_ratings",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_feedback_ratings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_feedback_ratings_question",
                schema: "trainer",
                table: "ai_feedback_ratings",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ux_ai_feedback_ratings_user_item",
                schema: "trainer",
                table: "ai_feedback_ratings",
                columns: new[] { "user_id", "session_item_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_feedback_ratings",
                schema: "trainer");
        }
    }
}
