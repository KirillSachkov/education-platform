using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TagService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityTagUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Restore unique index lost during migration squash (dce896c0).
            // Originally created in 20260310100544_Initial via raw SQL.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ux_entity_tags_entity_type_entity_id_tag_id
                ON tags.entity_tags (entity_type, entity_id, tag_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS tags.ux_entity_tags_entity_type_entity_id_tag_id;
                """);
        }
    }
}
