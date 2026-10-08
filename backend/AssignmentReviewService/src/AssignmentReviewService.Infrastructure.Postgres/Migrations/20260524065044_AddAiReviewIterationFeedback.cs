using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAiReviewIterationFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_review_iteration_feedback",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    iteration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_helpful = table.Column<bool>(type: "boolean", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_review_iteration_feedback", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_review_iteration_feedback_iteration",
                schema: "assignment_review",
                table: "ai_review_iteration_feedback",
                column: "iteration_id");

            migrationBuilder.CreateIndex(
                name: "uq_ai_review_iteration_feedback_iteration_user",
                schema: "assignment_review",
                table: "ai_review_iteration_feedback",
                columns: new[] { "iteration_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_review_iteration_feedback",
                schema: "assignment_review");
        }
    }
}
