using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropPlanKindAndAddSingletonIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the legacy `kind` column. Tier (Phase 1.1 backfill) is теперь
            //    source-of-truth и domain enum'е PlanKind удалён из кода.
            migrationBuilder.DropColumn(
                name: "kind",
                schema: "access",
                table: "plans");

            // 2. Cleanup duplicates перед singleton-index: для каждого автора с
            //    несколькими active+public+not-archived планами одного singleton-tier
            //    оставляем самый старый (по created_at), остальные unpublish'аем
            //    (is_public=false). Author может вручную опубликовать/архивировать
            //    через UI после миграции. На проде у одного автора нет дубликатов
            //    (audit'или 2026-05-08), но dev/staging могут иметь — этот шаг
            //    делает миграцию идемпотентной.
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY author_id, tier
                               ORDER BY created_at ASC
                           ) AS rn
                    FROM access.plans
                    WHERE is_active = TRUE
                      AND is_public = TRUE
                      AND archived_at IS NULL
                      AND tier IN ('FREE', 'LEARN_ALL', 'FULL_ALL')
                )
                UPDATE access.plans p
                SET is_public = FALSE
                FROM ranked r
                WHERE p.id = r.id AND r.rn > 1;
            ");

            // 3. Singleton-tier index (Phase 1.2): один active+public+not-archived план
            //    каждого из FREE / LEARN_ALL / FULL_ALL на автора. COURSE и SUBSCRIPTION
            //    без констрейнта — их может быть много. Domain validation в Plan.Publish
            //    бьёт `plan.tier.duplicate` до DB; этот индекс — финальный гейт от race.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_author_tier_singleton
                ON access.plans (author_id, tier)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND tier IN ('FREE', 'LEARN_ALL', 'FULL_ALL');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_tier_singleton;");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "access",
                table: "plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }
    }
}
