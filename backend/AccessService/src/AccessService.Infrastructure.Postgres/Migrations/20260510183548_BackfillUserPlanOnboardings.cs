using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class BackfillUserPlanOnboardings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill для legacy grant'ов: PlanGrantCreatedOnboardingHandler и таблица
            // user_plan_onboardings появились 2026-05-06 (issue #68). Grant'ы выпущенные
            // раньше, а также grant'ы выпущенные пока plan_onboarding_flows.is_enabled был
            // false, не имеют onboarding state. Создаём строки в Pending состоянии с
            // current_step_id = первый шаг по sort_order.
            //
            // Идемпотентно через NOT EXISTS — повторный прогон не дублирует.
            // Source=AUTO_FREE намеренно исключён — handler в production коде тоже его
            // пропускает (silent welcome flow для FREE-планов на регистрацию).
            migrationBuilder.Sql(
                """
                INSERT INTO access.user_plan_onboardings (
                    user_id, plan_id, started_at, completed_at,
                    current_step_id, completed_step_ids, skipped_step_ids
                )
                SELECT DISTINCT
                    g.user_id,
                    g.plan_id,
                    NOW(),
                    NULL::timestamptz,
                    (
                        SELECT s.id
                        FROM access.plan_onboarding_steps s
                        WHERE s.plan_id = g.plan_id
                        ORDER BY s.sort_order ASC
                        LIMIT 1
                    ),
                    ARRAY[]::uuid[],
                    ARRAY[]::uuid[]
                FROM access.plan_grants g
                INNER JOIN access.plan_onboarding_flows f ON f.plan_id = g.plan_id
                WHERE g.status = 'ACTIVE'
                  AND g.source <> 'AUTO_FREE'
                  AND f.is_enabled = TRUE
                  AND EXISTS (SELECT 1 FROM access.plan_onboarding_steps s WHERE s.plan_id = g.plan_id)
                  AND NOT EXISTS (
                      SELECT 1 FROM access.user_plan_onboardings o
                      WHERE o.user_id = g.user_id AND o.plan_id = g.plan_id
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down — no-op. Удалять backfilled строки опасно: в production юзеры могли
            // продвинуться по wizard'у после применения; rollback потеряет их прогресс.
        }
    }
}
