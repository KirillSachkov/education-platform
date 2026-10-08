using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanGrantPricePaidCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 2 #112: snapshot цены на момент выдачи grant'а. Используется
            // UpgradeCreditCalculator для расчёта credit при апгрейде на более широкий план.
            migrationBuilder.AddColumn<int>(
                name: "price_paid_cents",
                schema: "access",
                table: "plan_grants",
                type: "integer",
                nullable: true);

            // Backfill: для существующих PURCHASE-grants копируем актуальную цену плана.
            // Все остальные источники (ADMIN_GRANT, AUTO_FREE, TRIAL, GITHUB_ORG, TELEGRAM_F1,
            // INVITE_LINK, MIGRATION) оставляем NULL — они не платились пользователем.
            // На текущий момент на проде нет PURCHASE-grants (payment provider не подключён),
            // backfill — no-op в реальности, но идемпотентен на staging/dev.
            migrationBuilder.Sql(@"
                UPDATE access.plan_grants g
                SET price_paid_cents = p.price_cents
                FROM access.plans p
                WHERE g.plan_id = p.id
                  AND g.source = 'PURCHASE'
                  AND p.price_cents IS NOT NULL
                  AND p.price_cents > 0
                  AND g.price_paid_cents IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "price_paid_cents",
                schema: "access",
                table: "plan_grants");
        }
    }
}
