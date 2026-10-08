using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAiReviewPullRequestIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #713: hot-path lookup `GetLatestByPullRequestAsync` (вызывается на КАЖДОМ
            // webhook'е коммента студента) фильтрует по (provider, pull_number,
            // lower(repo_full_name)) и берёт свежайший по created_at. Repo-имена
            // регистронезависимы, поэтому запрос использует lower(repo_full_name) —
            // обычный btree по repo_full_name не подошёл бы. Functional composite index
            // покрывает equality-фильтр + ORDER BY created_at DESC LIMIT 1 без seq scan.
            // Fluent API не выражает lower()-выражение — поэтому raw SQL.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_ai_reviews_pr_lookup
                ON assignment_review.ai_reviews (provider, pull_number, lower(repo_full_name), created_at DESC);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS assignment_review.ix_ai_reviews_pr_lookup;");
        }
    }
}
