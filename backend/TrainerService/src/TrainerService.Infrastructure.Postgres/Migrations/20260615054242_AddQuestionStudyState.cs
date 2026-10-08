using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddQuestionStudyState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "question_study_states",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    times_seen = table.Column<int>(type: "integer", nullable: false),
                    times_known = table.Column<int>(type: "integer", nullable: false),
                    times_wrong = table.Column<int>(type: "integer", nullable: false),
                    ease_factor = table.Column<double>(type: "double precision", nullable: false),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    repetitions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_question_study_states", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_question_study_states_user_next_due",
                schema: "trainer",
                table: "question_study_states",
                columns: new[] { "user_id", "next_due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_question_study_states_user_status",
                schema: "trainer",
                table: "question_study_states",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_question_study_states_user_question",
                schema: "trainer",
                table: "question_study_states",
                columns: new[] { "user_id", "question_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "question_study_states",
                schema: "trainer");
        }
    }
}
