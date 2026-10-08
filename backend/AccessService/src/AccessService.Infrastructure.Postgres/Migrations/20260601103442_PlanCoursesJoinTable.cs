using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PlanCoursesJoinTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Join table for the COURSE-tier bundle (#404 — reverses #331's 1 plan = 1 course).
            //    Raw SQL (not Fluent CreateTable) so the FK + indexes match the EF configuration
            //    names exactly and the backfill can run in the same migration.
            migrationBuilder.Sql(@"
                CREATE TABLE access.plan_courses (
                    id        uuid NOT NULL,
                    plan_id   uuid NOT NULL,
                    course_id uuid NOT NULL,
                    CONSTRAINT ""PK_plan_courses"" PRIMARY KEY (id),
                    CONSTRAINT ""FK_plan_courses_plans_plan_id"" FOREIGN KEY (plan_id)
                        REFERENCES access.plans (id) ON DELETE CASCADE
                );
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ux_plan_courses_plan_course
                    ON access.plan_courses (plan_id, course_id);
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX ix_plan_courses_course_id
                    ON access.plan_courses (course_id);
            ");

            // 2. Backfill from the singular course_id column (every active COURSE binding).
            //    gen_random_uuid() is fine for a raw-SQL one-off backfill; runtime inserts use
            //    TimeOrderedGuidValueGenerator. ON CONFLICT keeps the migration re-runnable.
            migrationBuilder.Sql(@"
                INSERT INTO access.plan_courses (id, plan_id, course_id)
                SELECT gen_random_uuid(), id, course_id
                  FROM access.plans
                 WHERE tier = 'COURSE' AND course_id IS NOT NULL
                ON CONFLICT DO NOTHING;
            ");

            // 3. Drop the singular 1-plan-per-course guards — bundles intentionally remove them.
            migrationBuilder.Sql("ALTER TABLE access.plans DROP CONSTRAINT IF EXISTS plans_course_id_tier_check;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS access.ux_plans_course_active;");

            // 4. KEEP plans.course_id (rollback escape hatch) — it is simply no longer EF-mapped.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort restore of the singular model. course_id was never dropped, so we only
            // recreate the CHECK + per-course unique index and tear down the join table.
            migrationBuilder.Sql("DROP TABLE IF EXISTS access.plan_courses;");

            migrationBuilder.Sql(@"
                ALTER TABLE access.plans
                    ADD CONSTRAINT plans_course_id_tier_check CHECK (
                        (tier = 'COURSE' AND course_id IS NOT NULL) OR
                        (tier <> 'COURSE' AND course_id IS NULL)
                    );
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ux_plans_course_active
                    ON access.plans (author_id, course_id)
                 WHERE tier = 'COURSE' AND is_active = true AND archived_at IS NULL;
            ");
        }
    }
}
