using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Issue #580 (платный пробный месяц): пробный план — тоже tier FULL_ALL, но с
    /// <c>trial_duration_days</c> &gt; 0. Singleton-индекс <c>uq_plans_author_tier_singleton</c>
    /// (один active+public FULL_ALL/LEARN_ALL на автора) иначе блокировал бы его публикацию
    /// рядом с бессрочным FULL_ALL. Исключаем пробные планы из singleton-уникальности через
    /// <c>trial_duration_days IS NULL</c> — бессрочный план остаётся singleton'ом, пробный
    /// сосуществует. Дружелюбный code-check в <c>Publish.cs</c> симметрично пропускает пробные.
    /// Чистый index-rebuild, данные не трогаются.
    /// </remarks>
    public partial class RelaxSingletonIndexForTrialPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_tier_singleton;");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_author_tier_singleton
                ON access.plans (author_id, tier)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND trial_duration_days IS NULL
                  AND tier IN ('LEARN_ALL', 'FULL_ALL');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_tier_singleton;");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_author_tier_singleton
                ON access.plans (author_id, tier)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND tier IN ('LEARN_ALL', 'FULL_ALL');
            ");
        }
    }
}
