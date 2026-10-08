using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "target_author_id",
                schema: "comments",
                table: "comments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "author_feed_state",
                schema: "comments",
                columns: table => new
                {
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_author_feed_state", x => x.author_id);
                });

            // Composite filtered index supports the author-feed keyset query:
            // WHERE target_author_id = X AND is_deleted = false ORDER BY created_at DESC, id DESC
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_comments_target_author_id
                ON comments.comments (target_author_id, created_at DESC, id DESC)
                WHERE is_deleted = false AND target_author_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS comments.ix_comments_target_author_id;");

            migrationBuilder.DropTable(
                name: "author_feed_state",
                schema: "comments");

            migrationBuilder.DropColumn(
                name: "target_author_id",
                schema: "comments",
                table: "comments");
        }
    }
}
