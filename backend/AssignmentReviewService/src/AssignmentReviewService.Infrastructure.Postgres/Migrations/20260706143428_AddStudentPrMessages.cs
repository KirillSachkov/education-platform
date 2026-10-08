using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentPrMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "student_pr_messages",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    github_comment_id = table.Column<long>(type: "bigint", nullable: false),
                    in_reply_to_github_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    author_github_login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    line = table.Column<int>(type: "integer", nullable: true),
                    comment_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at_github = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    answer_body = table.Column<string>(type: "text", nullable: true),
                    answer_github_comment_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_student_pr_messages", x => x.id);
                    table.ForeignKey(
                        name: "FK_student_pr_messages_ai_reviews_ai_review_id",
                        column: x => x.ai_review_id,
                        principalSchema: "assignment_review",
                        principalTable: "ai_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_pr_messages_ai_review_id",
                schema: "assignment_review",
                table: "student_pr_messages",
                column: "ai_review_id");

            migrationBuilder.CreateIndex(
                name: "uq_student_pr_messages_github_comment_id",
                schema: "assignment_review",
                table: "student_pr_messages",
                column: "github_comment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "student_pr_messages",
                schema: "assignment_review");
        }
    }
}
