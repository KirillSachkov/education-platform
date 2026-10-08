using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseAssetOwnershipRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "asset_ownership_revision_seq",
                schema: "education");

            migrationBuilder.AddColumn<long>(
                name: "asset_ownership_revision",
                schema: "education",
                table: "courses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "asset_ownership_revision",
                schema: "education",
                table: "courses");

            migrationBuilder.DropSequence(
                name: "asset_ownership_revision_seq",
                schema: "education");
        }
    }
}
