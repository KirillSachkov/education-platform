using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentRecoveryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_idempotency_keys_order_id",
                schema: "access",
                table: "idempotency_keys",
                newName: "ix_idempotency_keys_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_rebill_recovery",
                schema: "access",
                table: "orders",
                columns: new[] { "status", "charge_type", "provider", "rebill_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_provider_created",
                schema: "access",
                table: "orders",
                columns: new[] { "status", "provider", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_order_events_reconciliation_deferral",
                schema: "access",
                table: "order_events",
                columns: new[] { "order_id", "event_type", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_rebill_recovery",
                schema: "access",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_status_provider_created",
                schema: "access",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_order_events_reconciliation_deferral",
                schema: "access",
                table: "order_events");

            migrationBuilder.RenameIndex(
                name: "ix_idempotency_keys_order_id",
                schema: "access",
                table: "idempotency_keys",
                newName: "IX_idempotency_keys_order_id");
        }
    }
}
