using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Каталожный дискриминатор (#674). DB-DEFAULT 'PLATFORM' страхует существующие строки и
            // legacy-вставки без явного значения; домен (ResolveScope) всё равно всегда пишет scope.
            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "access",
                table: "plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "PLATFORM");

            // Backfill: существующие TRAINER_PRO-офферы (подписка тренажёра) переводим в TRAINER-scope,
            // чтобы они сразу исчезли из платформенного каталога. Всё прочее остаётся PLATFORM (default).
            migrationBuilder.Sql(
                "UPDATE access.plans SET scope = 'TRAINER' WHERE offer_type = 'TRAINER_PRO';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "scope",
                schema: "access",
                table: "plans");
        }
    }
}
