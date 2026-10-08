using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAuditCorrelationId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                schema: "access",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                schema: "access",
                table: "order_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_correlation_id",
                schema: "access",
                table: "orders",
                column: "correlation_id",
                filter: "correlation_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_correlation_id",
                schema: "access",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "access",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                schema: "access",
                table: "order_events");
        }
    }
}
