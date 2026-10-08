using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentReviewPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE INDEX ix_ai_reviews_active_liveness
                ON assignment_review.ai_reviews ((GREATEST(updated_at, heartbeat_at)))
                WHERE status = 'RUNNING';
                """);

            migrationBuilder.Sql(
                """
                CREATE INDEX ix_ai_reviews_queued_updated_at
                ON assignment_review.ai_reviews (updated_at)
                WHERE status = 'QUEUED';
                """);

            migrationBuilder.CreateIndex(
                name: "ix_student_pr_messages_review_created",
                schema: "assignment_review",
                table: "student_pr_messages",
                columns: new[] { "ai_review_id", "created_at_github" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_reviews_author_id",
                schema: "assignment_review",
                table: "ai_reviews",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_reviews_issue_id",
                schema: "assignment_review",
                table: "ai_reviews",
                column: "issue_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_review_iterations_started_review",
                schema: "assignment_review",
                table: "ai_review_iterations",
                columns: new[] { "started_at", "ai_review_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX assignment_review.ix_ai_reviews_active_liveness;");

            migrationBuilder.Sql(
                "DROP INDEX assignment_review.ix_ai_reviews_queued_updated_at;");

            migrationBuilder.DropIndex(
                name: "ix_student_pr_messages_review_created",
                schema: "assignment_review",
                table: "student_pr_messages");

            migrationBuilder.DropIndex(
                name: "ix_ai_reviews_author_id",
                schema: "assignment_review",
                table: "ai_reviews");

            migrationBuilder.DropIndex(
                name: "ix_ai_reviews_issue_id",
                schema: "assignment_review",
                table: "ai_reviews");

            migrationBuilder.DropIndex(
                name: "ix_ai_review_iterations_started_review",
                schema: "assignment_review",
                table: "ai_review_iterations");
        }
    }
}
