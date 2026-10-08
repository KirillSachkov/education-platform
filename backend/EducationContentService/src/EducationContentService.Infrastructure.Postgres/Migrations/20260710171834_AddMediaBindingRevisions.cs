using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaBindingRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "image_binding_revision",
                schema: "education",
                table: "materials",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "media_version",
                schema: "education",
                table: "materials",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "video_binding_revision",
                schema: "education",
                table: "materials",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "image_binding_revision",
                schema: "education",
                table: "courses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "media_version",
                schema: "education",
                table: "courses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "video_binding_revision",
                schema: "education",
                table: "courses",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "cover_binding_revision",
                schema: "education",
                table: "collections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "media_version",
                schema: "education",
                table: "collections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_binding_revision",
                schema: "education",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "media_version",
                schema: "education",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "video_binding_revision",
                schema: "education",
                table: "materials");

            migrationBuilder.DropColumn(
                name: "image_binding_revision",
                schema: "education",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "media_version",
                schema: "education",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "video_binding_revision",
                schema: "education",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "cover_binding_revision",
                schema: "education",
                table: "collections");

            migrationBuilder.DropColumn(
                name: "media_version",
                schema: "education",
                table: "collections");
        }
    }
}
