using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentTargetDepthIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX ix_comments_target_depth
                ON comments.comments (target_entity_type, target_entity_id, depth, created_at, id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS comments.ix_comments_target_depth;
                """);
        }
    }
}
