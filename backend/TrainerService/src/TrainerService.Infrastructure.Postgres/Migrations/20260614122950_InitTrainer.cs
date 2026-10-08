using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitTrainer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trainer");

            migrationBuilder.CreateTable(
                name: "bookmarked_questions",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quiz_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookmarked_questions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "topic_banks",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quiz_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    difficulty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_banks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "topic_masteries",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mastery_percent = table.Column<int>(type: "integer", nullable: false),
                    answers_count = table.Column<int>(type: "integer", nullable: false),
                    last_practised_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_masteries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "topics",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    area = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recommended_course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fallback_course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topics", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_sessions",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    topic_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    score_percent = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "training_session_items",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quiz_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    question_text = table.Column<string>(type: "text", nullable: false),
                    options_json = table.Column<string>(type: "jsonb", nullable: false),
                    section = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    difficulty = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_index = table.Column<int>(type: "integer", nullable: false),
                    answer_raw = table.Column<string>(type: "text", nullable: true),
                    score_percent = table.Column<int>(type: "integer", nullable: true),
                    verdict = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    feedback = table.Column<string>(type: "text", nullable: true),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_training_session_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_training_session_items_training_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "trainer",
                        principalTable: "training_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_bookmarked_questions_user_quiz_question",
                schema: "trainer",
                table: "bookmarked_questions",
                columns: new[] { "user_id", "quiz_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_topic_banks_topic_id",
                schema: "trainer",
                table: "topic_banks",
                column: "topic_id");

            migrationBuilder.CreateIndex(
                name: "ux_topic_masteries_user_topic",
                schema: "trainer",
                table: "topic_masteries",
                columns: new[] { "user_id", "topic_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_topics_slug",
                schema: "trainer",
                table: "topics",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_training_session_items_session_sort",
                schema: "trainer",
                table: "training_session_items",
                columns: new[] { "session_id", "sort_index" });

            migrationBuilder.CreateIndex(
                name: "ix_training_sessions_user_id",
                schema: "trainer",
                table: "training_sessions",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookmarked_questions",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "topic_banks",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "topic_masteries",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "topics",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "training_session_items",
                schema: "trainer");

            migrationBuilder.DropTable(
                name: "training_sessions",
                schema: "trainer");
        }
    }
}
