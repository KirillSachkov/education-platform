using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionLifecycleState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "auto_renewal_cancelled_at",
                schema: "access",
                table: "plan_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "renewal_grace_ends_at",
                schema: "access",
                table: "plan_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE access.plan_grants
                SET charge_failure_count = LEAST(charge_failure_count, 3),
                    renewal_grace_ends_at = expires_at + INTERVAL '72 hours',
                    next_charge_at = CASE
                        WHEN charge_failure_count = 1 THEN expires_at
                        WHEN charge_failure_count = 2 THEN expires_at + INTERVAL '48 hours'
                        ELSE NULL
                    END
                WHERE status = 'ACTIVE'
                  AND rebill_id IS NOT NULL
                  AND customer_key IS NOT NULL
                  AND expires_at IS NOT NULL
                  AND charge_failure_count > 0;

                CREATE INDEX ix_plan_grants_due_recurring
                    ON access.plan_grants (next_charge_at)
                    WHERE status = 'ACTIVE'
                      AND rebill_id IS NOT NULL
                      AND customer_key IS NOT NULL
                      AND expires_at IS NOT NULL
                      AND next_charge_at IS NOT NULL
                      AND auto_renewal_cancelled_at IS NULL
                      AND charge_failure_count < 3;

                CREATE INDEX ix_plan_grants_effective_expiry
                    ON access.plan_grants (expires_at)
                    WHERE status = 'ACTIVE' AND expires_at IS NOT NULL;

                CREATE INDEX ix_plan_grants_grace_expiry
                    ON access.plan_grants (renewal_grace_ends_at)
                    WHERE status = 'ACTIVE' AND renewal_grace_ends_at IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS access.ix_plan_grants_due_recurring;
                DROP INDEX IF EXISTS access.ix_plan_grants_effective_expiry;
                DROP INDEX IF EXISTS access.ix_plan_grants_grace_expiry;
                """);

            migrationBuilder.DropColumn(
                name: "auto_renewal_cancelled_at",
                schema: "access",
                table: "plan_grants");

            migrationBuilder.DropColumn(
                name: "renewal_grace_ends_at",
                schema: "access",
                table: "plan_grants");
        }
    }
}
