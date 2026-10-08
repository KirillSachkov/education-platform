using AccessService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Single-platform catalog (#608): published platform plans are no longer scoped by
    /// author. FULL/LEARN and trial singleton rules now apply to the whole platform, and
    /// public plan slugs must be unambiguous because public detail routes no longer carry
    /// an author id.
    /// </remarks>
    [DbContext(typeof(AccessServiceDbContext))]
    [Migration("20260618120000_PlatformPlanSingletonIndexes")]
    public partial class PlatformPlanSingletonIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY slug
                               ORDER BY display_order ASC, created_at ASC, id ASC
                           ) AS rn
                    FROM access.plans
                    WHERE is_active = TRUE
                      AND is_public = TRUE
                      AND archived_at IS NULL
                )
                UPDATE access.plans p
                SET is_public = FALSE
                FROM ranked r
                WHERE p.id = r.id AND r.rn > 1;
            ");

            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               PARTITION BY tier
                               ORDER BY display_order ASC, created_at ASC, id ASC
                           ) AS rn
                    FROM access.plans
                    WHERE is_active = TRUE
                      AND is_public = TRUE
                      AND archived_at IS NULL
                      AND trial_duration_days IS NULL
                      AND tier IN ('LEARN_ALL', 'FULL_ALL')
                )
                UPDATE access.plans p
                SET is_public = FALSE
                FROM ranked r
                WHERE p.id = r.id AND r.rn > 1;
            ");

            migrationBuilder.Sql(@"
                WITH ranked AS (
                    SELECT id,
                           ROW_NUMBER() OVER (
                               ORDER BY display_order ASC, created_at ASC, id ASC
                           ) AS rn
                    FROM access.plans
                    WHERE is_active = TRUE
                      AND is_public = TRUE
                      AND archived_at IS NULL
                      AND trial_duration_days IS NOT NULL
                      AND tier = 'FULL_ALL'
                )
                UPDATE access.plans p
                SET is_public = FALSE
                FROM ranked r
                WHERE p.id = r.id AND r.rn > 1;
            ");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_tier_singleton;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_author_trial_singleton;");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_platform_public_slug
                ON access.plans (slug)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_platform_tier_singleton
                ON access.plans (tier)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND trial_duration_days IS NULL
                  AND tier IN ('LEARN_ALL', 'FULL_ALL');
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_platform_trial_singleton
                ON access.plans (tier)
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
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_platform_trial_singleton;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_platform_tier_singleton;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.uq_plans_platform_public_slug;");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX IF NOT EXISTS uq_plans_author_tier_singleton
                ON access.plans (author_id, tier)
                WHERE is_active = TRUE
                  AND is_public = TRUE
                  AND archived_at IS NULL
                  AND trial_duration_days IS NULL
                  AND tier IN ('LEARN_ALL', 'FULL_ALL');
            ");

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
    }
}
