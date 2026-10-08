using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class HardenPlanReferencesAndDeleteBarrier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "access",
                table: "plan_grants",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            // Non-financial projections can be safely removed before adding strict FKs.
            // Orders and grants are audit records and are deliberately preserved below.
            migrationBuilder.Sql("""
                DELETE FROM access.invite_redemptions r
                WHERE EXISTS (
                    SELECT 1
                    FROM access.invite_links l
                    WHERE l.id = r.invite_link_id
                      AND NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = l.plan_id));

                DELETE FROM access.github_org_invitations x
                WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = x.plan_id);
                DELETE FROM access.invite_links x
                WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = x.plan_id);
                DELETE FROM access.plan_pinned_materials x
                WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = x.plan_id);
                DELETE FROM access.user_plan_onboardings x
                WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = x.plan_id);
                DELETE FROM access.tg_join_reminders x
                WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = x.plan_id);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_user_plan_onboardings_plan_id",
                schema: "access",
                table: "user_plan_onboardings",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "IX_tg_join_reminders_plan_id",
                schema: "access",
                table: "tg_join_reminders",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_plan_id",
                schema: "access",
                table: "orders",
                column: "plan_id");

            migrationBuilder.AddForeignKey(
                name: "FK_github_org_invitations_plans_plan_id",
                schema: "access",
                table: "github_org_invitations",
                column: "plan_id",
                principalSchema: "access",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_invite_links_plans_plan_id",
                schema: "access",
                table: "invite_links",
                column: "plan_id",
                principalSchema: "access",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Preserve historical financial orphans. NOT VALID still protects every new
            // write; validate immediately only when existing data already satisfies it.
            migrationBuilder.Sql("""
                ALTER TABLE access.orders
                    ADD CONSTRAINT "FK_orders_plans_plan_id"
                    FOREIGN KEY (plan_id) REFERENCES access.plans(id)
                    ON DELETE RESTRICT NOT VALID;
                ALTER TABLE access.plan_grants
                    ADD CONSTRAINT "FK_plan_grants_plans_plan_id"
                    FOREIGN KEY (plan_id) REFERENCES access.plans(id)
                    ON DELETE RESTRICT NOT VALID;

                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM access.orders o
                        WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = o.plan_id))
                    THEN
                        ALTER TABLE access.orders
                            VALIDATE CONSTRAINT "FK_orders_plans_plan_id";
                    ELSE
                        RAISE WARNING 'FK_orders_plans_plan_id left NOT VALID: historical orphan orders preserved';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1 FROM access.plan_grants g
                        WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = g.plan_id))
                    THEN
                        ALTER TABLE access.plan_grants
                            VALIDATE CONSTRAINT "FK_plan_grants_plans_plan_id";
                    ELSE
                        RAISE WARNING 'FK_plan_grants_plans_plan_id left NOT VALID: historical orphan grants preserved';
                    END IF;
                END $$;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_plan_pinned_materials_plans_plan_id",
                schema: "access",
                table: "plan_pinned_materials",
                column: "plan_id",
                principalSchema: "access",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tg_join_reminders_plans_plan_id",
                schema: "access",
                table: "tg_join_reminders",
                column: "plan_id",
                principalSchema: "access",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_user_plan_onboardings_plans_plan_id",
                schema: "access",
                table: "user_plan_onboardings",
                column: "plan_id",
                principalSchema: "access",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_github_org_invitations_plans_plan_id",
                schema: "access",
                table: "github_org_invitations");

            migrationBuilder.DropForeignKey(
                name: "FK_invite_links_plans_plan_id",
                schema: "access",
                table: "invite_links");

            migrationBuilder.DropForeignKey(
                name: "FK_orders_plans_plan_id",
                schema: "access",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_plan_grants_plans_plan_id",
                schema: "access",
                table: "plan_grants");

            migrationBuilder.DropForeignKey(
                name: "FK_plan_pinned_materials_plans_plan_id",
                schema: "access",
                table: "plan_pinned_materials");

            migrationBuilder.DropForeignKey(
                name: "FK_tg_join_reminders_plans_plan_id",
                schema: "access",
                table: "tg_join_reminders");

            migrationBuilder.DropForeignKey(
                name: "FK_user_plan_onboardings_plans_plan_id",
                schema: "access",
                table: "user_plan_onboardings");

            migrationBuilder.DropIndex(
                name: "IX_user_plan_onboardings_plan_id",
                schema: "access",
                table: "user_plan_onboardings");

            migrationBuilder.DropIndex(
                name: "IX_tg_join_reminders_plan_id",
                schema: "access",
                table: "tg_join_reminders");

            migrationBuilder.DropIndex(
                name: "ix_orders_plan_id",
                schema: "access",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "access",
                table: "plan_grants");
        }
    }
}
