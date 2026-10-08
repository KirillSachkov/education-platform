using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ChangeOrderEventsFkToRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_events_orders_order_id",
                schema: "access",
                table: "order_events");

            migrationBuilder.AddForeignKey(
                name: "FK_order_events_orders_order_id",
                schema: "access",
                table: "order_events",
                column: "order_id",
                principalSchema: "access",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_order_events_orders_order_id",
                schema: "access",
                table: "order_events");

            migrationBuilder.AddForeignKey(
                name: "FK_order_events_orders_order_id",
                schema: "access",
                table: "order_events",
                column: "order_id",
                principalSchema: "access",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
