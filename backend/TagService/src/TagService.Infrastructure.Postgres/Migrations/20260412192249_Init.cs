using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TagService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tags");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, defaultValue: "CANON"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_tags",
                schema: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    entity_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_entity_tags", x => x.id);
                    table.ForeignKey(
                        name: "FK_entity_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "tags",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tag_aliases",
                schema: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alias_tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tag_aliases", x => x.id);
                    table.ForeignKey(
                        name: "FK_tag_aliases_tags_alias_tag_id",
                        column: x => x.alias_tag_id,
                        principalSchema: "tags",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_tag_aliases_tags_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "tags",
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_tags_tag_id",
                schema: "tags",
                table: "entity_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "IX_tag_aliases_alias_tag_id",
                schema: "tags",
                table: "tag_aliases",
                column: "alias_tag_id");

            migrationBuilder.CreateIndex(
                name: "ux_tag_aliases_tag_id_alias_tag_id",
                schema: "tags",
                table: "tag_aliases",
                columns: new[] { "tag_id", "alias_tag_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_author_id",
                schema: "tags",
                table: "tags",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_title_trgm",
                schema: "tags",
                table: "tags",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ux_tags_slug",
                schema: "tags",
                table: "tags",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_tags",
                schema: "tags");

            migrationBuilder.DropTable(
                name: "tag_aliases",
                schema: "tags");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "tags");
        }
    }
}
