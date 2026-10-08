using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SearchService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddReindexAutoInvalidationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "applied_deploy_stamp",
                schema: "search",
                table: "reindex_state",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "applied_schema_hash",
                schema: "search",
                table: "reindex_state",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "applied_deploy_stamp",
                schema: "search",
                table: "reindex_state");

            migrationBuilder.DropColumn(
                name: "applied_schema_hash",
                schema: "search",
                table: "reindex_state");
        }
    }
}
