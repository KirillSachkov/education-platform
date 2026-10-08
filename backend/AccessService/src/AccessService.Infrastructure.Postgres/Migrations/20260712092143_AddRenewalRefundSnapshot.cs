using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddRenewalRefundSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "renewal_grant_id",
                schema: "access",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "renewal_previous_expires_at",
                schema: "access",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "renewal_target_expires_at",
                schema: "access",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_orders_renewal_grant_id",
                schema: "access",
                table: "orders",
                column: "renewal_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_renewal_refund_reconciliation",
                schema: "access",
                table: "orders",
                columns: new[] { "status", "charge_type", "provider", "created_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_orders_plan_grants_renewal_grant_id",
                schema: "access",
                table: "orders",
                column: "renewal_grant_id",
                principalSchema: "access",
                principalTable: "plan_grants",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_orders_plan_grants_renewal_grant_id",
                schema: "access",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_orders_renewal_grant_id",
                schema: "access",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_renewal_refund_reconciliation",
                schema: "access",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "renewal_grant_id",
                schema: "access",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "renewal_previous_expires_at",
                schema: "access",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "renewal_target_expires_at",
                schema: "access",
                table: "orders");
        }
    }
}
