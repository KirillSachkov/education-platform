using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class OwnQuestionBank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_bookmarked_questions_user_quiz_question",
                schema: "trainer",
                table: "bookmarked_questions");

            migrationBuilder.DropColumn(
                name: "quiz_id",
                schema: "trainer",
                table: "training_session_items");

            migrationBuilder.DropColumn(
                name: "quiz_id",
                schema: "trainer",
                table: "topic_banks");

            migrationBuilder.DropColumn(
                name: "quiz_id",
                schema: "trainer",
                table: "mock_interview_questions");

            migrationBuilder.DropColumn(
                name: "quiz_id",
                schema: "trainer",
                table: "bookmarked_questions");

            migrationBuilder.CreateTable(
                name: "trainer_questions",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bank_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stem = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_answer = table.Column<string>(type: "text", nullable: true),
                    explanation = table.Column<string>(type: "text", nullable: true),
                    difficulty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trainer_questions", x => x.id);
                    table.ForeignKey(
                        name: "FK_trainer_questions_topic_banks_bank_id",
                        column: x => x.bank_id,
                        principalSchema: "trainer",
                        principalTable: "topic_banks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trainer_question_options",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    sort_index = table.Column<int>(type: "integer", nullable: false),
                    trainer_question_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trainer_question_options", x => x.id);
                    table.ForeignKey(
                        name: "FK_trainer_question_options_trainer_questions_trainer_question~",
                        column: x => x.trainer_question_id,
                        principalSchema: "trainer",
                        principalTable: "trainer_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_bookmarked_questions_user_question",
                schema: "trainer",
                table: "bookmarked_questions",
                columns: new[] { "user_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_trainer_question_options_question_sort",
                schema: "trainer",
                table: "trainer_question_options",
                columns: new[] { "trainer_question_id", "sort_index" });

            migrationBuilder.CreateIndex(
                name: "IX_trainer_questions_bank_id",
                schema: "trainer",
                table: "trainer_questions",
                column: "bank_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trainer_question_options",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "trainer_questions",
                schema: "trainer");

            migrationBuilder.DropIndex(
                name: "ux_bookmarked_questions_user_question",
                schema: "trainer",
                table: "bookmarked_questions");

            migrationBuilder.AddColumn<Guid>(
                name: "quiz_id",
                schema: "trainer",
                table: "training_session_items",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "quiz_id",
                schema: "trainer",
                table: "topic_banks",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "quiz_id",
                schema: "trainer",
                table: "mock_interview_questions",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<Guid>(
                name: "quiz_id",
                schema: "trainer",
                table: "bookmarked_questions",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "ux_bookmarked_questions_user_quiz_question",
                schema: "trainer",
                table: "bookmarked_questions",
                columns: new[] { "user_id", "quiz_id", "question_id" },
                unique: true);
        }
    }
}
