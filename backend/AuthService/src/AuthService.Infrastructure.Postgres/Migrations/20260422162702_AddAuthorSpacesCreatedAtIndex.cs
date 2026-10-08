using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <summary>
    ///     Composite index on (created_at, id) for deterministic paging of author_spaces.
    /// </summary>
    public partial class AddAuthorSpacesCreatedAtIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_author_spaces_created_at_id
                    ON auth.author_spaces (created_at, id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS auth.ix_author_spaces_created_at_id;");
        }
    }
}
