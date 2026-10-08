using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RequireTrainerQuestionOptionParent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "trainer_question_id",
                schema: "trainer",
                table: "trainer_question_options",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "trainer_question_id",
                schema: "trainer",
                table: "trainer_question_options",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}
