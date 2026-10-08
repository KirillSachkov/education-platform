using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EducationContentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class SquashedInit_20260502 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "education");

            migrationBuilder.CreateTable(
                name: "courses",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    image_id = table.Column<Guid>(type: "uuid", nullable: true),
                    video_id = table.Column<Guid>(type: "uuid", nullable: true),
                    getting_started_module_id = table.Column<Guid>(type: "uuid", nullable: true),
                    github_org_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_trial_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_new = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "issues",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    access_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "ENROLLED"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    external_links = table.Column<string>(type: "jsonb", nullable: true),
                    internal_materials = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_issues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", maxLength: 50000, nullable: true),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    access_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    image_id = table.Column<Guid>(type: "uuid", nullable: true),
                    video_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materials", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "modules",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    detailed_description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_modules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    detailed_description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quizzes",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quizzes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roadmaps",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roadmaps", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "collections",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", maxLength: 2000, nullable: true),
                    cover_image_id = table.Column<Guid>(type: "uuid", nullable: true),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    access_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_pinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    pinned_sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, collation: "C"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collections", x => x.id);
                    table.ForeignKey(
                        name: "FK_collections_courses_course_id",
                        column: x => x.course_id,
                        principalSchema: "education",
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "course_items",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C"),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_course_items_courses_course_id",
                        column: x => x.course_id,
                        principalSchema: "education",
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "course_materials",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_course_materials_courses_course_id",
                        column: x => x.course_id,
                        principalSchema: "education",
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "module_items",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C"),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    view_priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Recommended")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_module_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_module_items_modules_module_id",
                        column: x => x.module_id,
                        principalSchema: "education",
                        principalTable: "modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_items",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C"),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    max_score = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_items_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "education",
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "roadmap_edges",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    roadmap_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    edge_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    animated = table.Column<bool>(type: "boolean", nullable: false),
                    source_handle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    target_handle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roadmap_edges", x => x.id);
                    table.ForeignKey(
                        name: "FK_roadmap_edges_roadmaps_roadmap_id",
                        column: x => x.roadmap_id,
                        principalSchema: "education",
                        principalTable: "roadmaps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roadmap_nodes",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    roadmap_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    position_x = table.Column<double>(type: "double precision", nullable: false),
                    position_y = table.Column<double>(type: "double precision", nullable: false),
                    width = table.Column<double>(type: "double precision", nullable: true),
                    height = table.Column<double>(type: "double precision", nullable: true),
                    parent_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roadmap_nodes", x => x.id);
                    table.ForeignKey(
                        name: "FK_roadmap_nodes_roadmaps_roadmap_id",
                        column: x => x.roadmap_id,
                        principalSchema: "education",
                        principalTable: "roadmaps",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_sections",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    collection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_sections", x => x.id);
                    table.ForeignKey(
                        name: "FK_collection_sections_collections_collection_id",
                        column: x => x.collection_id,
                        principalSchema: "education",
                        principalTable: "collections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "collection_items",
                schema: "education",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    section_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sort_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "C")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_collection_items_collection_sections_section_id",
                        column: x => x.section_id,
                        principalSchema: "education",
                        principalTable: "collection_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_collection_items_materials_material_id",
                        column: x => x.material_id,
                        principalSchema: "education",
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_collection_items_material_id",
                schema: "education",
                table: "collection_items",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "IX_collection_items_section_id_sort_key",
                schema: "education",
                table: "collection_items",
                columns: new[] { "section_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ux_collection_items_section_material",
                schema: "education",
                table: "collection_items",
                columns: new[] { "section_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_collection_sections_collection_id_sort_key",
                schema: "education",
                table: "collection_sections",
                columns: new[] { "collection_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_collections_author_id",
                schema: "education",
                table: "collections",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_collections_author_status",
                schema: "education",
                table: "collections",
                columns: new[] { "author_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_collections_course_id",
                schema: "education",
                table: "collections",
                column: "course_id",
                filter: "course_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_course_items_course_id_sort_key",
                schema: "education",
                table: "course_items",
                columns: new[] { "course_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "IX_course_materials_course_id_material_id",
                schema: "education",
                table: "course_materials",
                columns: new[] { "course_id", "material_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_course_materials_course_id_sort_key",
                schema: "education",
                table: "course_materials",
                columns: new[] { "course_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_courses_author_id",
                schema: "education",
                table: "courses",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_courses_slug_unique",
                schema: "education",
                table: "courses",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_courses_title",
                schema: "education",
                table: "courses",
                column: "title",
                unique: true,
                filter: "status <> 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "ix_issues_author_id",
                schema: "education",
                table: "issues",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_issues_project_id",
                schema: "education",
                table: "issues",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_issues_title",
                schema: "education",
                table: "issues",
                columns: new[] { "project_id", "title" },
                unique: true,
                filter: "status <> 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "ix_materials_author_id",
                schema: "education",
                table: "materials",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_materials_kind_author_id",
                schema: "education",
                table: "materials",
                columns: new[] { "kind", "author_id" });

            migrationBuilder.CreateIndex(
                name: "ix_materials_title",
                schema: "education",
                table: "materials",
                column: "title",
                unique: true,
                filter: "status IN ('PUBLISHED', 'ARCHIVED')");

            migrationBuilder.CreateIndex(
                name: "IX_module_items_module_id_sort_key",
                schema: "education",
                table: "module_items",
                columns: new[] { "module_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ux_module_items_issue_reference",
                schema: "education",
                table: "module_items",
                column: "reference_id",
                unique: true,
                filter: "item_type = 'Issue'");

            migrationBuilder.CreateIndex(
                name: "ix_modules_author_id",
                schema: "education",
                table: "modules",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_modules_title",
                schema: "education",
                table: "modules",
                column: "title",
                unique: true,
                filter: "status <> 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "IX_project_items_project_id_sort_key",
                schema: "education",
                table: "project_items",
                columns: new[] { "project_id", "sort_key" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_author_id",
                schema: "education",
                table: "projects",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_projects_title",
                schema: "education",
                table: "projects",
                column: "title",
                unique: true,
                filter: "status <> 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "ix_quizzes_author_id",
                schema: "education",
                table: "quizzes",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_quizzes_title",
                schema: "education",
                table: "quizzes",
                column: "title",
                unique: true,
                filter: "status <> 'DRAFT'");

            migrationBuilder.CreateIndex(
                name: "IX_roadmap_edges_roadmap_id",
                schema: "education",
                table: "roadmap_edges",
                column: "roadmap_id");

            migrationBuilder.CreateIndex(
                name: "IX_roadmap_nodes_parent_node_id",
                schema: "education",
                table: "roadmap_nodes",
                column: "parent_node_id");

            migrationBuilder.CreateIndex(
                name: "IX_roadmap_nodes_roadmap_id",
                schema: "education",
                table: "roadmap_nodes",
                column: "roadmap_id");

            migrationBuilder.CreateIndex(
                name: "ix_roadmaps_author_id",
                schema: "education",
                table: "roadmaps",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_roadmaps_course_id_unique",
                schema: "education",
                table: "roadmaps",
                column: "course_id",
                unique: true,
                filter: "course_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_roadmaps_slug_unique",
                schema: "education",
                table: "roadmaps",
                column: "slug",
                unique: true,
                filter: "slug IS NOT NULL");

            // GIN trigram index for ILIKE '%substr%' search on materials.title.
            // pg_trgm extension is provisioned in docker/postgres/init-databases.sql
            // (CREATE EXTENSION pg_trgm SCHEMA public). Test fixtures rely on Testcontainers
            // bringing up the same init script, so the extension is always available.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_materials_title_trgm
                    ON education.materials
                    USING gin (lower(title) gin_trgm_ops);
                """);

            // Cursor pagination indexes (DESC ordering + partial filters not expressible via Fluent API).
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_collections_updated_at_id
                    ON education.collections (updated_at DESC, id DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_courses_created_at_id
                    ON education.courses (created_at DESC, id DESC)
                    WHERE status = 'PUBLISHED';
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_courses_my_updated_at_id
                    ON education.courses (author_id, updated_at DESC, id DESC);
                """);

            // Recent materials feed (partial composite with DESC).
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_materials_published_at
                    ON education.materials (published_at DESC, id DESC)
                    WHERE status = 'PUBLISHED' AND published_at IS NOT NULL;
                """);

            // Pinned collections per course (partial composite).
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_collections_pinned
                    ON education.collections (course_id, pinned_sort_key)
                    WHERE is_pinned = true AND course_id IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS education.ix_collections_pinned;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS education.ix_materials_published_at;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS education.ix_courses_my_updated_at_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS education.ix_courses_created_at_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS education.ix_collections_updated_at_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS education.ix_materials_title_trgm;");

            migrationBuilder.DropTable(
                name: "collection_items",
                schema: "education");

            migrationBuilder.DropTable(
                name: "course_items",
                schema: "education");

            migrationBuilder.DropTable(
                name: "course_materials",
                schema: "education");

            migrationBuilder.DropTable(
                name: "issues",
                schema: "education");

            migrationBuilder.DropTable(
                name: "module_items",
                schema: "education");

            migrationBuilder.DropTable(
                name: "project_items",
                schema: "education");

            migrationBuilder.DropTable(
                name: "quizzes",
                schema: "education");

            migrationBuilder.DropTable(
                name: "roadmap_edges",
                schema: "education");

            migrationBuilder.DropTable(
                name: "roadmap_nodes",
                schema: "education");

            migrationBuilder.DropTable(
                name: "collection_sections",
                schema: "education");

            migrationBuilder.DropTable(
                name: "materials",
                schema: "education");

            migrationBuilder.DropTable(
                name: "modules",
                schema: "education");

            migrationBuilder.DropTable(
                name: "projects",
                schema: "education");

            migrationBuilder.DropTable(
                name: "roadmaps",
                schema: "education");

            migrationBuilder.DropTable(
                name: "collections",
                schema: "education");

            migrationBuilder.DropTable(
                name: "courses",
                schema: "education");
        }
    }
}
