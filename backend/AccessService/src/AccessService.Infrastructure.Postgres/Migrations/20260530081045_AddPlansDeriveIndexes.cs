using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <remarks>
    /// access-derive-model (#367): ростер курса и «мои курсы» деривируются на чтение из
    /// <c>plans × plan_grants</c>. Derive-запрос (<c>PlanGrantsRepository.BuildUserKeysetGrouping</c>)
    /// бьёт по <c>plans</c> двумя ветками БЕЗ фильтра <c>is_active</c> (grant может оставаться ACTIVE
    /// даже после архивации плана), поэтому существующие partial-индексы (<c>uq_plans_author_tier_singleton</c>,
    /// <c>ux_plans_course_active</c> — оба требуют <c>is_active=TRUE</c>) его не покрывают. Два partial-индекса
    /// под точную форму предиката. На single-tenant с десятками планов сейчас незаметно (seq-scan мгновенный),
    /// добавлено до роста таблицы. <c>IF NOT EXISTS</c> — идемпотентность для migration-контейнера.
    /// </remarks>
    /// <inheritdoc />
    public partial class AddPlansDeriveIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Lifetime-ветка derive-запроса: WHERE archived_at IS NULL AND tier IN ('FULL_ALL','LEARN_ALL') AND author_id = @authorId
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_plans_lifetime_author_derive
                ON access.plans (author_id)
                WHERE archived_at IS NULL
                  AND tier IN ('FULL_ALL', 'LEARN_ALL');
            ");

            // COURSE-ветка derive-запроса: WHERE archived_at IS NULL AND tier = 'COURSE' AND course_id = @courseId
            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS ix_plans_course_derive
                ON access.plans (course_id)
                WHERE archived_at IS NULL
                  AND tier = 'COURSE';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.ix_plans_course_derive;");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS access.ix_plans_lifetime_author_derive;");
        }
    }
}
