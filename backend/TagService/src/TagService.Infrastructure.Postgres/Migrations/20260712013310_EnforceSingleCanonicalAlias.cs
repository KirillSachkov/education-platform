using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TagService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleCanonicalAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tag_aliases_alias_tag_id",
                schema: "tags",
                table: "tag_aliases");

            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (PARTITION BY alias_tag_id ORDER BY id) AS row_number
                    FROM tags.tag_aliases
                )
                DELETE FROM tags.tag_aliases duplicate
                USING ranked
                WHERE duplicate.id = ranked.id
                  AND ranked.row_number > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ux_tag_aliases_alias_tag_id",
                schema: "tags",
                table: "tag_aliases",
                column: "alias_tag_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_tag_aliases_alias_tag_id",
                schema: "tags",
                table: "tag_aliases");

            migrationBuilder.CreateIndex(
                name: "IX_tag_aliases_alias_tag_id",
                schema: "tags",
                table: "tag_aliases",
                column: "alias_tag_id");
        }
    }
}
