using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class MakeSessionTrackIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "track_id",
                schema: "trainer",
                table: "training_sessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "track_id",
                schema: "trainer",
                table: "training_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
