using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanGrantUpgradeBasePrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "upgrade_base_price_cents",
                schema: "access",
                table: "plan_grants",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE access.plan_grants AS g
                SET upgrade_base_price_cents = target.effective_price_cents
                FROM access.plans AS trial
                JOIN LATERAL (
                    SELECT
                        CASE
                            WHEN full_plan.discount_percent BETWEEN 1 AND 99
                                 AND full_plan.discount_starts_at IS NOT NULL
                                 AND full_plan.discount_ends_at IS NOT NULL
                                 AND NOW() >= full_plan.discount_starts_at
                                 AND NOW() < full_plan.discount_ends_at
                            THEN GREATEST(
                                0,
                                full_plan.price_cents - ((full_plan.price_cents * full_plan.discount_percent) / 100)
                            )
                            ELSE full_plan.price_cents
                        END AS effective_price_cents
                    FROM access.plans AS full_plan
                    WHERE full_plan.author_id = trial.author_id
                      AND full_plan.tier = 'FULL_ALL'
                      AND full_plan.trial_duration_days IS NULL
                      AND full_plan.archived_at IS NULL
                      AND full_plan.is_active = TRUE
                      AND full_plan.is_public = TRUE
                      AND full_plan.price_cents IS NOT NULL
                      AND full_plan.price_cents > 0
                    ORDER BY full_plan.is_highlighted DESC, full_plan.display_order, full_plan.created_at
                    LIMIT 1
                ) AS target ON TRUE
                WHERE g.plan_id = trial.id
                  AND trial.tier = 'FULL_ALL'
                  AND trial.trial_duration_days IS NOT NULL
                  AND g.source = 'PURCHASE'
                  AND g.price_paid_cents IS NOT NULL
                  AND g.price_paid_cents > 0
                  AND g.upgrade_base_price_cents IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "upgrade_base_price_cents",
                schema: "access",
                table: "plan_grants");
        }
    }
}
