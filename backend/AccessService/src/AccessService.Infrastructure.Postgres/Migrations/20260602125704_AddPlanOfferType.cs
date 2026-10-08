using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanOfferType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DB-default 'COURSE' заполняет существующие строки (NOT NULL) И намеренно
            // ОСТАЁТСЯ после миграции. Домен пишет offer_type явно (Create/UpdateOfferType),
            // НО raw-SQL вставки планов (тест-сидеры вроде SeedFreePlanRawAsync, seed-CLI,
            // disaster-recovery) колонку не указывают — без DB-default они бьются о NOT NULL
            // (поймано CI: GetCourseGranteesTests.Free_grant_is_not_in_roster). Правило
            // «без HasDefaultValue в EF config» про EF-модель, а не про DB-default миграции —
            // не нарушено.
            migrationBuilder.AddColumn<string>(
                name: "offer_type",
                schema: "access",
                table: "plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "COURSE");

            // Backfill: FULL_ALL / LEARN_ALL планы — это «полный доступ» оффер.
            migrationBuilder.Sql(
                "UPDATE access.plans SET offer_type = 'FULL_ACCESS' WHERE tier IN ('FULL_ALL', 'LEARN_ALL');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "offer_type",
                schema: "access",
                table: "plans");
        }
    }
}
