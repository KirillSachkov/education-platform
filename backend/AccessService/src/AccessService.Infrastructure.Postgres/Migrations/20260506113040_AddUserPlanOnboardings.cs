using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPlanOnboardings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_plan_onboardings",
                schema: "access",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_step_id = table.Column<Guid>(type: "uuid", nullable: true),
                    skipped_step_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    completed_step_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_plan_onboardings", x => new { x.user_id, x.plan_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_plan_onboardings_user_pending",
                schema: "access",
                table: "user_plan_onboardings",
                columns: new[] { "user_id", "started_at" },
                filter: "completed_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_plan_onboardings",
                schema: "access");
        }
    }
}
