using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PlanCheckConstraintRelaxArchived : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Relax `plans_course_id_tier_check` to allow archived rows with any course_id state.
            // Original constraint hard-failed if a COURSE plan ended up with NULL course_id (which
            // can happen if a pre-migration disaster-recovery insert / seed script bypassed domain
            // validation, leaving a row with empty `course_ids[]`). The active path still enforces
            // the singular CourseId invariant; archived rows are preserved as audit trail.
            migrationBuilder.Sql("ALTER TABLE access.plans DROP CONSTRAINT IF EXISTS plans_course_id_tier_check;");

            // Defensive: archive any orphaned COURSE plans that somehow have NULL course_id.
            // Should be 0 per pre-migration audit; this is a safety net.
            migrationBuilder.Sql(@"
                UPDATE access.plans
                   SET is_active = false,
                       archived_at = now()
                 WHERE tier = 'COURSE'
                   AND course_id IS NULL
                   AND archived_at IS NULL;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE access.plans
                    ADD CONSTRAINT plans_course_id_tier_check CHECK (
                        archived_at IS NOT NULL
                        OR (tier = 'COURSE' AND course_id IS NOT NULL)
                        OR (tier <> 'COURSE' AND course_id IS NULL)
                    );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE access.plans DROP CONSTRAINT IF EXISTS plans_course_id_tier_check;");

            migrationBuilder.Sql(@"
                ALTER TABLE access.plans
                    ADD CONSTRAINT plans_course_id_tier_check CHECK (
                        (tier = 'COURSE' AND course_id IS NOT NULL)
                        OR (tier <> 'COURSE' AND course_id IS NULL)
                    );
            ");
        }
    }
}
