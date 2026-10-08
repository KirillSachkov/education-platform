using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plan_onboarding_flows",
                schema: "access",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_onboarding_flows", x => x.plan_id);
                    table.ForeignKey(
                        name: "FK_plan_onboarding_flows_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "access",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_onboarding_steps",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    step_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_skippable = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_onboarding_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_plan_onboarding_steps_plan_onboarding_flows_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "access",
                        principalTable: "plan_onboarding_flows",
                        principalColumn: "plan_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plan_onboarding_steps_plan_order",
                schema: "access",
                table: "plan_onboarding_steps",
                columns: new[] { "plan_id", "sort_order" });

            // Один TELEGRAM/GITHUB/NOTIFICATIONS на план — partial-unique.
            // MARKDOWN — без ограничений. Нужен raw SQL, потому что Fluent API
            // не умеет combinated partial-WHERE по enum-значениям.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX ix_plan_onboarding_steps_unique_kind
                ON access.plan_onboarding_steps (plan_id, step_type)
                WHERE step_type IN ('TELEGRAM', 'GITHUB', 'NOTIFICATIONS');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS access.ix_plan_onboarding_steps_unique_kind;");

            migrationBuilder.DropTable(
                name: "plan_onboarding_steps",
                schema: "access");

            migrationBuilder.DropTable(
                name: "plan_onboarding_flows",
                schema: "access");
        }
    }
}
