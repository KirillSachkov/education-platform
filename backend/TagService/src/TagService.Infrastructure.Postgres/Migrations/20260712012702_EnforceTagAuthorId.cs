using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TagService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EnforceTagAuthorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "ck_tags_author_id_not_empty",
                schema: "tags",
                table: "tags",
                sql: "author_id <> '00000000-0000-0000-0000-000000000000'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_tags_author_id_not_empty",
                schema: "tags",
                table: "tags");
        }
    }
}
