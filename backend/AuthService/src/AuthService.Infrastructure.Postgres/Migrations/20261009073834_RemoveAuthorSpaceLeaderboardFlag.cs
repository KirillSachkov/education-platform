using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveAuthorSpaceLeaderboardFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // JSON storage remains; remove only the retired flag, preserving all other values.
            migrationBuilder.Sql("""
                UPDATE auth.author_spaces
                SET feature_flags = feature_flags - 'Leaderboard' - 'leaderboard'
                WHERE feature_flags ? 'Leaderboard' OR feature_flags ? 'leaderboard';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The JSON column is unchanged. Removed flag values require a backup to restore.
        }
    }
}