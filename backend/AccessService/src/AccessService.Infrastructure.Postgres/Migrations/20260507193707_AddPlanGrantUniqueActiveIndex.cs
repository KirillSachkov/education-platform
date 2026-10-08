using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanGrantUniqueActiveIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "uq_plan_grants_user_plan_active",
                schema: "access",
                table: "plan_grants",
                columns: new[] { "user_id", "plan_id" },
                unique: true,
                filter: "status = 'ACTIVE'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_plan_grants_user_plan_active",
                schema: "access",
                table: "plan_grants");
        }
    }
}
