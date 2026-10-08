using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanTierColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "tier",
                schema: "access",
                table: "plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Backfill — derive tier from (kind, capabilities). Соответствует
            // Plan.DeriveTier в domain. После Phase 1.3 kind будет dropнут.
            //
            // PlanCapabilities — [Flags] int. FULL = 0b111111 = 63 (все возможности).
            // LEARN_ONLY = VIEW_MATERIALS = 0b000001 = 1 (только просмотр).
            //
            // Маппинг:
            //   FREE          → FREE
            //   SUBSCRIPTION  → SUBSCRIPTION
            //   COURSES       → COURSE
            //   LIFETIME_ALL + capabilities=1 (LEARN_ONLY) → LEARN_ALL
            //   LIFETIME_ALL + иначе                       → FULL_ALL
            migrationBuilder.Sql(@"
                UPDATE access.plans
                SET tier = CASE
                    WHEN kind = 'FREE' THEN 'FREE'
                    WHEN kind = 'SUBSCRIPTION' THEN 'SUBSCRIPTION'
                    WHEN kind = 'COURSES' THEN 'COURSE'
                    WHEN kind = 'LIFETIME_ALL' AND capabilities = 1 THEN 'LEARN_ALL'
                    WHEN kind = 'LIFETIME_ALL' THEN 'FULL_ALL'
                    ELSE 'FULL_ALL'
                END
                WHERE tier = '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tier",
                schema: "access",
                table: "plans");
        }
    }
}
