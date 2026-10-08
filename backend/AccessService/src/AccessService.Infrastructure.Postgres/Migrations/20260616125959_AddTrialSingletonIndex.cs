using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Issue #595 (пробный доступ — singleton на автора): пробный план — это tier FULL_ALL
    /// с <c>trial_duration_days</c> &gt; 0. Бессрочный FULL_ALL/LEARN_ALL покрыт индексом
    /// <c>uq_plans_author_tier_singleton</c> (там фильтр <c>trial_duration_days IS NULL</c>),
    /// поэтому пробные планы в него не попадают и могли бы плодиться. Этот индекс делает
    /// пробный план тоже singleton'ом: один active+public пробный FULL_ALL на автора.
    /// Дружелюбный code-check в <c>Publish.cs</c> (ветка <c>plan.IsTrial</c>) бьёт
    /// <c>plan.trial.duplicate</c> до DB-уровня; индекс — финальный гейт от гонки.
    /// Чистый index-only, данные не трогаются.
    /// </remarks>
    public partial class AddTrialSingletonIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_author_trial_singleton
                ON access.plans (author_id)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND trial_duration_days IS NOT NULL
                  AND tier = 'FULL_ALL';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_trial_singleton;");
        }
    }
}
