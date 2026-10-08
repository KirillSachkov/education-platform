using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SearchService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddReindexStateTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.CreateTable(
                name: "reindex_state",
                schema: "search",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    applied_generation = table.Column<int>(type: "integer", nullable: false),
                    last_applied_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_request_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reindex_state", x => x.id);
                    table.CheckConstraint("ck_reindex_state_singleton", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reindex_state",
                schema: "search");
        }
    }
}
