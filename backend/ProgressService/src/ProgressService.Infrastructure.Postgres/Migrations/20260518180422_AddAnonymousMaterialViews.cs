using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAnonymousMaterialViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "anonymous_material_views",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anonymous_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_anonymous_material_views", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_anonymous_material_views_material_id",
                schema: "progress",
                table: "anonymous_material_views",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_anonymous_material_views_anon_id_material_id",
                schema: "progress",
                table: "anonymous_material_views",
                columns: new[] { "anonymous_id", "material_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "anonymous_material_views",
                schema: "progress");
        }
    }
}
