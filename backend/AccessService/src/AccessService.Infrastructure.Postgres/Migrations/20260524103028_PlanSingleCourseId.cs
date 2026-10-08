using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class PlanSingleCourseId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "course_id",
                schema: "access",
                table: "plans",
                type: "uuid",
                nullable: true);

            // Data copy: for COURSE-tier plans with a non-empty array, take the first element.
            // Pre-migration audit `backend/AccessService/scripts/check-multicourse-plans.sql`
            // confirms multi-course COURSE plans don't exist; first element is the canonical
            // course binding.
            migrationBuilder.Sql(@"
                UPDATE access.plans
                   SET course_id = course_ids[1]
                 WHERE tier = 'COURSE'
                   AND array_length(course_ids, 1) >= 1;
            ");

            migrationBuilder.DropColumn(
                name: "course_ids",
                schema: "access",
                table: "plans");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS access.ux_plans_course_active;");
            migrationBuilder.Sql("ALTER TABLE access.plans DROP CONSTRAINT IF EXISTS plans_course_id_tier_check;");

            migrationBuilder.AddColumn<Guid[]>(
                name: "course_ids",
                schema: "access",
                table: "plans",
                type: "uuid[]",
                nullable: false,
                defaultValueSql: "'{}'::uuid[]");

            migrationBuilder.Sql(@"
                UPDATE access.plans
                   SET course_ids = ARRAY[course_id]
                 WHERE tier = 'COURSE' AND course_id IS NOT NULL;
            ");

            migrationBuilder.DropColumn(
                name: "course_id",
                schema: "access",
                table: "plans");
        }
    }
}
