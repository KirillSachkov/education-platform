using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialCreditOverrideAndReminder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "credit_override_until",
                schema: "access",
                table: "plan_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expiry_reminder_sent_at",
                schema: "access",
                table: "plan_grants",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "credit_override_until",
                schema: "access",
                table: "plan_grants");

            migrationBuilder.DropColumn(
                name: "expiry_reminder_sent_at",
                schema: "access",
                table: "plan_grants");
        }
    }
}
