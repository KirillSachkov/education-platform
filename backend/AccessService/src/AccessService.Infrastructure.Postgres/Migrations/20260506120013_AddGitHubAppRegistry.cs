using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubAppRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "author_github_installations",
                schema: "access",
                columns: table => new
                {
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    installation_id = table.Column<long>(type: "bigint", nullable: false),
                    org_login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_author_github_installations", x => x.author_id);
                });

            migrationBuilder.CreateTable(
                name: "github_org_invitations",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    github_login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    org_login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    github_invitation_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_org_invitations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_author_github_installations_installation_id",
                schema: "access",
                table: "author_github_installations",
                column: "installation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_author_github_installations_org_login",
                schema: "access",
                table: "author_github_installations",
                column: "org_login");

            migrationBuilder.CreateIndex(
                name: "ix_github_org_invitations_pending",
                schema: "access",
                table: "github_org_invitations",
                columns: new[] { "org_login", "github_login" });

            migrationBuilder.CreateIndex(
                name: "ix_github_org_invitations_user_plan",
                schema: "access",
                table: "github_org_invitations",
                columns: new[] { "plan_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "author_github_installations",
                schema: "access");

            migrationBuilder.DropTable(
                name: "github_org_invitations",
                schema: "access");
        }
    }
}
