using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAccessSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "access");

            migrationBuilder.CreateTable(
                name: "invite_links",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(22)", maxLength: 22, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    multi_use = table.Column<bool>(type: "boolean", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invite_links", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "invite_redemptions",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invite_link_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_grant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redeemed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ip_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invite_redemptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plan_grants",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_ref = table.Column<Guid>(type: "uuid", nullable: true),
                    granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_grants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                schema: "access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    short_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    long_description = table.Column<string>(type: "text", nullable: false),
                    cover_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    features = table.Column<string>(type: "jsonb", nullable: false),
                    price_cents = table.Column<int>(type: "integer", nullable: true),
                    currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    course_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    includes_future_content = table.Column<bool>(type: "boolean", nullable: false),
                    capability_profile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    term_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    term_recurring_days = table.Column<int>(type: "integer", nullable: true),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plans", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invite_links_plan_id",
                schema: "access",
                table: "invite_links",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_invite_links_token",
                schema: "access",
                table: "invite_links",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invite_redemptions_invite_user",
                schema: "access",
                table: "invite_redemptions",
                columns: new[] { "invite_link_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_grants_plan_id",
                schema: "access",
                table: "plan_grants",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_grants_user_status",
                schema: "access",
                table: "plan_grants",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_plans_is_public",
                schema: "access",
                table: "plans",
                column: "is_public",
                filter: "is_active");

            // Composite unique on (author_id, slug). The slug column is generated from the
            // owned PlanSlug value object, so EF Fluent API can't express a composite index
            // that mixes a plain property and an owned-type property.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS ix_plans_author_slug ON access.plans (author_id, slug)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS access.ix_plans_author_slug");

            migrationBuilder.DropTable(
                name: "invite_links",
                schema: "access");

            migrationBuilder.DropTable(
                name: "invite_redemptions",
                schema: "access");

            migrationBuilder.DropTable(
                name: "plan_grants",
                schema: "access");

            migrationBuilder.DropTable(
                name: "plans",
                schema: "access");
        }
    }
}
