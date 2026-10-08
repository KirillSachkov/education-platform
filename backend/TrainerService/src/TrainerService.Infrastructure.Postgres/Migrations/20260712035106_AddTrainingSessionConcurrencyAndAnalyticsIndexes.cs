using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TrainerService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTrainingSessionConcurrencyAndAnalyticsIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "version",
                schema: "trainer",
                table: "training_sessions",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateIndex(
                name: "ix_training_sessions_completed_at",
                schema: "trainer",
                table: "training_sessions",
                column: "completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_training_sessions_grading_status",
                schema: "trainer",
                table: "training_sessions",
                column: "grading_status");

            migrationBuilder.CreateIndex(
                name: "ix_training_sessions_started_at",
                schema: "trainer",
                table: "training_sessions",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_training_session_items_question_id",
                schema: "trainer",
                table: "training_session_items",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_feedback_ratings_created_at",
                schema: "trainer",
                table: "ai_feedback_ratings",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_training_sessions_completed_at",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropIndex(
                name: "ix_training_sessions_grading_status",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropIndex(
                name: "ix_training_sessions_started_at",
                schema: "trainer",
                table: "training_sessions");

            migrationBuilder.DropIndex(
                name: "ix_training_session_items_question_id",
                schema: "trainer",
                table: "training_session_items");

            migrationBuilder.DropIndex(
                name: "ix_ai_feedback_ratings_created_at",
                schema: "trainer",
                table: "ai_feedback_ratings");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "trainer",
                table: "training_sessions");
        }
    }
}
