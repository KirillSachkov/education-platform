using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGamificationAndLevelTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "level_test_attempts",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "progress_users",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "user_gamification_stats",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "xp_awards",
                schema: "progress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "level_test_attempts",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_grading_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    anonymous_id = table.Column<Guid>(type: "uuid", nullable: true),
                    answers = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    claimed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    grading_config = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    overall_percent = table.Column<int>(type: "integer", nullable: false),
                    question_results = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    quiz_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recommended_course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    section_scores = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_level_test_attempts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "progress_users",
                schema: "progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    username = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_progress_users", x => x.user_id);
                });

            migrationBuilder.CreateTable(
                name: "user_gamification_stats",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    current_level = table.Column<int>(type: "integer", nullable: false),
                    total_xp = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_gamification_stats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "xp_awards",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    award_type = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    xp_amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_xp_awards", x => x.id);
                    table.ForeignKey(
                        name: "fk_xp_awards_course_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "progress",
                        principalTable: "course_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_level_test_attempts_anonymous_id_unclaimed",
                schema: "progress",
                table: "level_test_attempts",
                column: "anonymous_id",
                filter: "user_id IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_progress_users_username",
                schema: "progress",
                table: "progress_users",
                column: "username");

            migrationBuilder.CreateIndex(
                name: "ix_user_gamification_stats_leaderboard",
                schema: "progress",
                table: "user_gamification_stats",
                columns: new[] { "total_xp", "updated_at", "user_id" },
                descending: new[] { true, false, false });

            migrationBuilder.CreateIndex(
                name: "ux_user_gamification_stats_user_id",
                schema: "progress",
                table: "user_gamification_stats",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_xp_awards_enrollment_id",
                schema: "progress",
                table: "xp_awards",
                column: "enrollment_id");

            migrationBuilder.CreateIndex(
                name: "ix_xp_awards_user_id",
                schema: "progress",
                table: "xp_awards",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_xp_awards_user_id_award_type_source_id",
                schema: "progress",
                table: "xp_awards",
                columns: new[] { "user_id", "award_type", "source_id" },
                unique: true);
        }
    }
}
