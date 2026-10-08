using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanPromotionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "discount_ends_at",
                schema: "access",
                table: "plans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "discount_percent",
                schema: "access",
                table: "plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "discount_starts_at",
                schema: "access",
                table: "plans",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "discount_ends_at",
                schema: "access",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "discount_percent",
                schema: "access",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "discount_starts_at",
                schema: "access",
                table: "plans");
        }
    }
}
