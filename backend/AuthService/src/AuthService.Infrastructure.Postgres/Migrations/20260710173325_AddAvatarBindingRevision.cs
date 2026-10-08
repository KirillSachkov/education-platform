using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAvatarBindingRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "avatar_binding_revision",
                schema: "auth",
                table: "user_profiles",
                type: "bigint",
                nullable: false,
                defaultValue: -1L);

            migrationBuilder.Sql(
                """
                UPDATE auth.user_profiles
                SET avatar_binding_revision = 0
                WHERE avatar_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_binding_revision",
                schema: "auth",
                table: "user_profiles");
        }
    }
}
