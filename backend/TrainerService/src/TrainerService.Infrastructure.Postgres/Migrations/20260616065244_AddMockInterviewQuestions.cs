using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMockInterviewQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "questions_per_session",
                schema: "trainer",
                table: "mock_interviews",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "mock_interview_questions",
                schema: "trainer",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    quiz_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_index = table.Column<int>(type: "integer", nullable: false),
                    mock_interview_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mock_interview_questions", x => x.id);
                    table.ForeignKey(
                        name: "FK_mock_interview_questions_mock_interviews_mock_interview_id",
                        column: x => x.mock_interview_id,
                        principalSchema: "trainer",
                        principalTable: "mock_interviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mock_interview_questions_interview_sort",
                schema: "trainer",
                table: "mock_interview_questions",
                columns: new[] { "mock_interview_id", "sort_index" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mock_interview_questions",
                schema: "trainer");

            migrationBuilder.DropColumn(
                name: "questions_per_session",
                schema: "trainer",
                table: "mock_interviews");
        }
    }
}
