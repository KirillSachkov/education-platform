using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseEnrollmentSortKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add column nullable first so we can backfill before NOT NULL.
            migrationBuilder.AddColumn<string>(
                name: "sort_key",
                schema: "progress",
                table: "course_enrollments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                collation: "C");

            // 2. Backfill: per user, assign fractional-indexing keys ordered by enrolled_at.
            //    a0..a9, aA..aZ, aa..az (62 slots), then b00..bzz (62*62 slots).
            //    Covers up to 62 + 62*62 = 3906 enrollments per user. If any user exceeds
            //    that, the migration raises — handle via CLI utility.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    base62 CONSTANT text := '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz';
                    max_per_user int;
                BEGIN
                    SELECT COALESCE(MAX(cnt), 0) INTO max_per_user
                    FROM (
                        SELECT COUNT(*) AS cnt FROM progress.course_enrollments GROUP BY user_id
                    ) g;

                    IF max_per_user > 62 + 62 * 62 THEN
                        RAISE EXCEPTION
                            'Enrollment backfill: user has % enrollments, exceeds 3906 supported by inline backfill. '
                            'Run a CLI backfill instead.', max_per_user;
                    END IF;

                    UPDATE progress.course_enrollments c
                    SET sort_key = k.new_sort_key
                    FROM (
                        SELECT
                            id,
                            CASE
                                WHEN rn < 62 THEN
                                    'a' || substring(base62 FROM rn::int + 1 FOR 1)
                                ELSE
                                    'b'
                                        || substring(base62 FROM ((rn - 62) / 62)::int + 1 FOR 1)
                                        || substring(base62 FROM ((rn - 62) % 62)::int + 1 FOR 1)
                            END AS new_sort_key
                        FROM (
                            SELECT
                                id,
                                row_number() OVER (PARTITION BY user_id ORDER BY enrolled_at, id) - 1 AS rn
                            FROM progress.course_enrollments
                        ) ordered
                    ) k
                    WHERE c.id = k.id;
                END $$;
            """);

            // 3. Lock down the column as NOT NULL now that backfill is complete.
            migrationBuilder.AlterColumn<string>(
                name: "sort_key",
                schema: "progress",
                table: "course_enrollments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true,
                collation: "C",
                oldCollation: "C");

            migrationBuilder.CreateIndex(
                name: "ix_course_enrollments_user_id_sort_key",
                schema: "progress",
                table: "course_enrollments",
                columns: new[] { "user_id", "sort_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_course_enrollments_user_id_sort_key",
                schema: "progress",
                table: "course_enrollments");

            migrationBuilder.DropColumn(
                name: "sort_key",
                schema: "progress",
                table: "course_enrollments");
        }
    }
}
