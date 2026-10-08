using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropGithubOrgFromCourses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill: переносим course.github_org_slug → plan.github_org_slug (#69 cutover).
            // Один LIFETIME_ALL plan на автора получит первый non-null org_slug у любого его курса.
            // Если у автора несколько курсов с разными org_slug — берём произвольный (DISTINCT ON по
            // author_id), остальные теряются (это редкий corner-case — ops может допривязать вручную).
            // IF EXISTS на access.plans для тестовых БД, где этой schema может не быть.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.tables
                        WHERE table_schema = 'access' AND table_name = 'plans'
                    ) THEN
                        UPDATE access.plans p
                        SET github_org_slug = src.github_org_slug
                        FROM (
                            SELECT DISTINCT ON (c.author_id)
                                c.author_id,
                                lower(trim(c.github_org_slug)) AS github_org_slug
                            FROM education.courses c
                            WHERE c.github_org_slug IS NOT NULL
                              AND length(trim(c.github_org_slug)) > 0
                            ORDER BY c.author_id, c.created_at ASC
                        ) src
                        WHERE p.author_id = src.author_id
                          AND p.kind = 'LIFETIME_ALL'
                          AND p.archived_at IS NULL
                          AND p.github_org_slug IS NULL
                          AND NOT EXISTS (
                              -- partial-unique index ix_plans_github_org_slug_active защищает от
                              -- conflict'а, но pre-проверка даёт чище error message в редком случае
                              SELECT 1 FROM access.plans p2
                              WHERE p2.github_org_slug = src.github_org_slug
                                AND p2.archived_at IS NULL
                          );
                    END IF;
                END $$;
                """);

            migrationBuilder.DropColumn(
                name: "github_org_slug",
                schema: "education",
                table: "courses");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "github_org_slug",
                schema: "education",
                table: "courses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
