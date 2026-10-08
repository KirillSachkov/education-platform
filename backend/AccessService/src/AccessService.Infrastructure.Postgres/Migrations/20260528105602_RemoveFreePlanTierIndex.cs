using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Issue #358: убрать FREE-tier из singleton-tier index и archived'нуть существующие
    /// FREE-планы + revoke AUTO_FREE grants. После этой миграции FREE-планы остаются
    /// в БД (legacy), но новых не создаётся (<c>Plan.Create</c> возвращает <c>plan.free.deprecated</c>),
    /// а оставшиеся active AUTO_FREE grants переведены в REVOKED — это синхронизирует
    /// с удалением <c>PlanGrantSource.AUTO_FREE</c> из enum.
    ///
    /// Redis-теги (<c>plan:free:author_*</c>) у активных юзеров не чистятся этой миграцией —
    /// для этого см. CLI <c>cleanup-free-tags</c> в AccessService.Web.
    /// </remarks>
    public partial class RemoveFreePlanTierIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the old singleton index that included FREE, recreate without it.
            //    Migration name `DropPlanKindAndAddSingletonIndex` (2026-05-08) created
            //    `uq_plans_author_tier_singleton` для FREE / LEARN_ALL / FULL_ALL.
            //    После #358 FREE-tier deprecated, оставляем только LEARN_ALL / FULL_ALL.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_tier_singleton;");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_author_tier_singleton
                ON access.plans (author_id, tier)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND tier IN ('LEARN_ALL', 'FULL_ALL');
            ");

            // 2. Archive любые существующие FREE-планы (active или public), чтобы
            //    Plan.Create-блок и удаление AUTO_FREE source были консистентны.
            migrationBuilder.Sql(@"
                UPDATE access.plans
                SET is_active = FALSE,
                    is_public = FALSE,
                    archived_at = NOW()
                WHERE tier = 'FREE'
                  AND archived_at IS NULL;
            ");

            // 3. Revoke любые ACTIVE AUTO_FREE grants — они были выпущены IssueAutoFreeGrants*
            //    handler'ами, которые удалены вместе с этим тиром.
            migrationBuilder.Sql(@"
                UPDATE access.plan_grants
                SET status = 'REVOKED',
                    revoked_at = NOW(),
                    revoke_reason = 'free_plan_removed_issue_358'
                WHERE source = 'AUTO_FREE'
                  AND status = 'ACTIVE';
            ");

            // 4. Re-tag legacy source='AUTO_FREE' rows to MIGRATION — `PlanGrantSource.AUTO_FREE`
            //    enum value удалён в #358, и `HasConversion<string>()` на колонке source
            //    бросает ArgumentException при чтении row с unknown enum-значением. MIGRATION
            //    — ближайший legacy-эквивалент по семантике (backfill из legacy state).
            //    `revoke_reason='free_plan_removed_issue_358'` сохраняет audit-контекст.
            //    Также обновляем уже REVOKED строки с source='AUTO_FREE' (legacy state до этой миграции).
            migrationBuilder.Sql(@"
                UPDATE access.plan_grants
                SET source = 'MIGRATION'
                WHERE source = 'AUTO_FREE';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Откатываем только индекс (включаем FREE обратно). Archived FREE-планы и
            // revoked AUTO_FREE grants — manual recovery only: восстанавливать массово
            // нельзя, потому что часть grants могла быть revoked'нута по другим причинам.
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_tier_singleton;");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_author_tier_singleton
                ON access.plans (author_id, tier)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND tier IN ('FREE', 'LEARN_ALL', 'FULL_ALL');
            ");
        }
    }
}
