using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RetireAuthorRoadmapsFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE auth.author_spaces
                SET feature_flags = feature_flags - 'Roadmaps' - 'roadmaps'
                WHERE feature_flags ? 'Roadmaps' OR feature_flags ? 'roadmaps';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The JSON column is unchanged. Removed flag values require a backup to restore.
        }
    }
}