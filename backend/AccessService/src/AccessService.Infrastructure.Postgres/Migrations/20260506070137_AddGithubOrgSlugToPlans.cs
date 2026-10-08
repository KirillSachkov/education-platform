using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddGithubOrgSlugToPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "github_org_slug",
                schema: "access",
                table: "plans",
                type: "character varying(39)",
                maxLength: 39,
                nullable: true);

            // Partial-unique index: один org привязан к максимум одному активному плану.
            // Архивированные планы исключены, чтобы повторная привязка после Archive() была валидной.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ix_plans_github_org_slug_active
                ON access.plans (github_org_slug)
                WHERE github_org_slug IS NOT NULL AND archived_at IS NULL;
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS access.ix_plans_github_org_slug_active;");

            migrationBuilder.DropColumn(
                name: "github_org_slug",
                schema: "access",
                table: "plans");
        }
    }
}
