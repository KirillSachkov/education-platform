using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanGrantsUserPlanIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Non-unique composite (user_id, plan_id). Партиальный unique-индекс
            // uq_plan_grants_user_plan_active не помогает планировщику когда query
            // НЕ фильтрует по status — например ExistsAsync(user_id == X && plan_id == Y)
            // в IssueAutoFreeGrantsOnUser{Created,LoggedIn}Handler. На каждом логине
            // юзера handler делает 1 вызов на N FREE-планов; без этого индекса
            // получаем N*seqscan-like reads. См. issue #228 MSG-1.
            //
            // EF Core не задаёт этот индекс в Fluent API: HasIndex дедуплицирует
            // по совпадающему набору колонок с существующим partial-unique.
            // Создаём raw SQL'ом.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_plan_grants_user_plan
                ON access.plan_grants (user_id, plan_id);
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS access.ix_plan_grants_user_plan;");
        }
    }
}
