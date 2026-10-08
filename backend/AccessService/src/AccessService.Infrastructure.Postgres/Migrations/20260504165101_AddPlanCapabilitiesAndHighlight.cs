using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanCapabilitiesAndHighlight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order matters: add columns → backfill → drop legacy column.
            migrationBuilder.AddColumn<int>(
                name: "capabilities",
                schema: "access",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_highlighted",
                schema: "access",
                table: "plans",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: LEARN_ONLY profile → VIEW_MATERIALS only (1).
            // FULL profile или любой другой → FULL bitmask (63 = 1|2|4|8|16|32).
            // Соответствует: VIEW_MATERIALS | SUBMIT_ISSUES | CODE_REVIEW |
            // COMMUNITY_ACCESS | LIVE_CALLS | JOB_SUPPORT.
            migrationBuilder.Sql(@"
                UPDATE access.plans
                SET capabilities = CASE
                    WHEN kind = 'FREE' THEN 1
                    WHEN capability_profile = 'LEARN_ONLY' THEN 1
                    ELSE 63
                END;
            ");

            migrationBuilder.DropColumn(
                name: "capability_profile",
                schema: "access",
                table: "plans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Восстанавливаем legacy колонку с backfill из bitmask.
            // capabilities = 1 (только VIEW_MATERIALS) → 'LEARN_ONLY'; всё остальное → 'FULL'.
            // Это lossy: индивидуальные комбинации флагов (например, VIEW + SUBMIT_ISSUES)
            // схлопываются в FULL — точное восстановление невозможно через старую модель
            // record-обёртки `{ Profile }`. Откат требует prior backup.
            migrationBuilder.AddColumn<string>(
                name: "capability_profile",
                schema: "access",
                table: "plans",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "FULL");

            migrationBuilder.Sql(@"
                UPDATE access.plans
                SET capability_profile = CASE
                    WHEN capabilities = 1 THEN 'LEARN_ONLY'
                    ELSE 'FULL'
                END;
            ");

            migrationBuilder.DropColumn(
                name: "capabilities",
                schema: "access",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "is_highlighted",
                schema: "access",
                table: "plans");
        }
    }
}
