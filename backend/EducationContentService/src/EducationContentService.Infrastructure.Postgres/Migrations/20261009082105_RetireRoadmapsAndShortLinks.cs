using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RetireRoadmapsAndShortLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "roadmap_edges",
                schema: "education");

            migrationBuilder.DropTable(
                name: "roadmap_nodes",
                schema: "education");

            migrationBuilder.DropTable(
                name: "short_links",
                schema: "education");

            migrationBuilder.DropTable(
                name: "roadmaps",
                schema: "education");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreates the retired schema only; removed rows require a verified backup to restore.
            migrationBuilder.CreateTable(
                name: "roadmaps",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roadmaps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "short_links",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_short_links", x => x.id);
                    table.ForeignKey(
                        name: "FK_short_links_materials_material_id",
                        column: x => x.material_id,
                        principalSchema: "education",
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roadmap_edges",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    animated = table.Column<bool>(type: "boolean", nullable: false),
                    edge_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    roadmap_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_handle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    source_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_handle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    target_node_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roadmap_edges", x => x.id);
                    table.ForeignKey(
                        name: "FK_roadmap_edges_roadmaps_roadmap_id",
                        column: x => x.roadmap_id,
                        principalSchema: "education",
                        principalTable: "roadmaps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roadmap_nodes",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    height = table.Column<double>(type: "double precision", nullable: true),
                    node_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parent_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position_x = table.Column<double>(type: "double precision", nullable: false),
                    position_y = table.Column<double>(type: "double precision", nullable: false),
                    roadmap_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roadmap_nodes", x => x.id);
                    table.ForeignKey(
                        name: "FK_roadmap_nodes_roadmaps_roadmap_id",
                        column: x => x.roadmap_id,
                        principalSchema: "education",
                        principalTable: "roadmaps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_roadmap_edges_roadmap_id",
                schema: "education",
                table: "roadmap_edges",
                column: "roadmap_id");

            migrationBuilder.CreateIndex(
                name: "IX_roadmap_nodes_parent_node_id",
                schema: "education",
                table: "roadmap_nodes",
                column: "parent_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_roadmap_nodes_roadmap_id",
                schema: "education",
                table: "roadmap_nodes",
                column: "roadmap_id");

            migrationBuilder.CreateIndex(
                name: "ix_roadmaps_author_id",
                schema: "education",
                table: "roadmaps",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_roadmaps_course_id_unique",
                schema: "education",
                table: "roadmaps",
                column: "course_id",
                unique: true,
                filter: "course_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_roadmaps_slug_unique",
                schema: "education",
                table: "roadmaps",
                column: "slug",
                unique: true,
                filter: "slug IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_short_links_code",
                schema: "education",
                table: "short_links",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_short_links_material_id",
                schema: "education",
                table: "short_links",
                column: "material_id",
                unique: true);
        }
    }
}