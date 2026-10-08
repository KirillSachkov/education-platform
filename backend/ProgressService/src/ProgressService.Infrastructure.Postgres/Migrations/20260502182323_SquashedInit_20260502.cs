using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProgressService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SquashedInit_20260502 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "progress");

            migrationBuilder.CreateTable(
                name: "content_grants",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    grant_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    granted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_content_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "course_enrollments",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    enrolled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    suspended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_enrollments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "course_positions",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "material_bookmarks",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_entity_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_bookmarks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "material_views",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    viewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_views", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "progress_users",
                schema: "progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    display_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
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
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_xp = table.Column<int>(type: "integer", nullable: false),
                    current_level = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_gamification_stats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "issue_progress",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_progress_course_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "progress",
                        principalTable: "course_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_item_progress",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_item_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_module_item_progress_course_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "progress",
                        principalTable: "course_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "module_progress",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    items_total = table.Column<int>(type: "integer", nullable: false),
                    items_completed = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_module_progress_course_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "progress",
                        principalTable: "course_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_progress",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    total_issues_count = table.Column<int>(type: "integer", nullable: false),
                    total_issues_completed = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_progress_course_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "progress",
                        principalTable: "course_enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "xp_awards",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    award_type = table.Column<string>(type: "text", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xp_amount = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "issue_submissions",
                schema: "progress",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    issue_progress_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    review_status = table.Column<string>(type: "text", nullable: false),
                    reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    review_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    feedback = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issue_submissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_issue_submissions_issue_progress_issue_progress_id",
                        column: x => x.issue_progress_id,
                        principalSchema: "progress",
                        principalTable: "issue_progress",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_content_grants_user_resource",
                schema: "progress",
                table: "content_grants",
                columns: new[] { "user_id", "resource_type", "resource_id" },
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_course_enrollments_author_id",
                schema: "progress",
                table: "course_enrollments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_enrollments_course_id",
                schema: "progress",
                table: "course_enrollments",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ux_course_enrollments_user_id_course_id",
                schema: "progress",
                table: "course_enrollments",
                columns: new[] { "user_id", "course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_course_positions_user_id_course_id",
                schema: "progress",
                table: "course_positions",
                columns: new[] { "user_id", "course_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_issue_progress_enrollment_id_project_id",
                schema: "progress",
                table: "issue_progress",
                columns: new[] { "enrollment_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "ux_issue_progress_enrollment_id_issue_id",
                schema: "progress",
                table: "issue_progress",
                columns: new[] { "enrollment_id", "issue_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_issue_submissions_review_status_reviewed_at",
                schema: "progress",
                table: "issue_submissions",
                columns: new[] { "review_status", "reviewed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_issue_submissions_review_status_submitted_at",
                schema: "progress",
                table: "issue_submissions",
                columns: new[] { "review_status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ux_issue_submissions_issue_progress_id_attempt_number",
                schema: "progress",
                table: "issue_submissions",
                columns: new[] { "issue_progress_id", "attempt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_material_bookmarks_user_id_created_at_id",
                schema: "progress",
                table: "material_bookmarks",
                columns: new[] { "user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_material_views_material_id",
                schema: "progress",
                table: "material_views",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "ux_material_views_user_id_material_id",
                schema: "progress",
                table: "material_views",
                columns: new[] { "user_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_module_item_progress_enrollment_id_reference_id_item_type",
                schema: "progress",
                table: "module_item_progress",
                columns: new[] { "enrollment_id", "reference_id", "item_type" });

            migrationBuilder.CreateIndex(
                name: "ux_module_item_progress_enrollment_id_module_id_reference_id_item_type",
                schema: "progress",
                table: "module_item_progress",
                columns: new[] { "enrollment_id", "module_id", "reference_id", "item_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_module_progress_enrollment_id_module_id",
                schema: "progress",
                table: "module_progress",
                columns: new[] { "enrollment_id", "module_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_progress_users_username",
                schema: "progress",
                table: "progress_users",
                column: "username");

            migrationBuilder.CreateIndex(
                name: "ux_project_progress_enrollment_id_project_id",
                schema: "progress",
                table: "project_progress",
                columns: new[] { "enrollment_id", "project_id" },
                unique: true);

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

            // material_bookmarks unique index — `(user_id, course_id, target_entity_type, target_entity_id)`.
            // Backs ON CONFLICT upsert in MaterialBookmarkRepository. EF Fluent API cannot express
            // this composite (owned-type navigation + parent-scoped scalars), so created via raw SQL.
            // PostgreSQL auto-truncates the index name to 63 chars (NAMEDATALEN-1).
            // See backend/ProgressService/.../MaterialBookmarkConfiguration.cs note.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ux_material_bookmarks_user_id_course_id_target_entity_type_target_entity_id
                ON progress.material_bookmarks (user_id, course_id, target_entity_type, target_entity_id);
                """);

            // GIN trigram indexes for ILIKE '%substr%' search on progress_users.
            // pg_trgm extension is provisioned by docker/postgres/init-databases.sql.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_progress_users_username_trgm
                    ON progress.progress_users USING gin (username gin_trgm_ops);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_progress_users_display_name_trgm
                    ON progress.progress_users USING gin (display_name gin_trgm_ops);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS progress.ix_progress_users_display_name_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS progress.ix_progress_users_username_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS progress.ux_material_bookmarks_user_id_course_id_target_entity_type_target_entity_id;");

            migrationBuilder.DropTable(
                name: "content_grants",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "course_positions",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "issue_submissions",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "material_bookmarks",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "material_views",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "module_item_progress",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "module_progress",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "progress_users",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "project_progress",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "user_gamification_stats",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "xp_awards",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "issue_progress",
                schema: "progress");

            migrationBuilder.DropTable(
                name: "course_enrollments",
                schema: "progress");
        }
    }
}
