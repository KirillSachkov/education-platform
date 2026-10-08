using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringSubscriptionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "charge_failure_count",
                schema: "access",
                table: "plan_grants",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "customer_key",
                schema: "access",
                table: "plan_grants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_charge_at",
                schema: "access",
                table: "plan_grants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rebill_id",
                schema: "access",
                table: "plan_grants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "charge_type",
                schema: "access",
                table: "orders",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "INITIAL");

            migrationBuilder.AddColumn<string>(
                name: "rebill_id",
                schema: "access",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "charge_failure_count",
                schema: "access",
                table: "plan_grants");

            migrationBuilder.DropColumn(
                name: "customer_key",
                schema: "access",
                table: "plan_grants");

            migrationBuilder.DropColumn(
                name: "next_charge_at",
                schema: "access",
                table: "plan_grants");

            migrationBuilder.DropColumn(
                name: "rebill_id",
                schema: "access",
                table: "plan_grants");

            migrationBuilder.DropColumn(
                name: "charge_type",
                schema: "access",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "rebill_id",
                schema: "access",
                table: "orders");
        }
    }
}
