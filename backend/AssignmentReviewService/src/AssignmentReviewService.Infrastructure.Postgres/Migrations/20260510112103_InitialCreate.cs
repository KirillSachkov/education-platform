using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assignment_review");

            migrationBuilder.CreateTable(
                name: "ai_model_settings",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reviewer_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reviewer_temperature = table.Column<double>(type: "double precision", nullable: true),
                    reviewer_max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    reviewer_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    triage_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    triage_temperature = table.Column<double>(type: "double precision", nullable: true),
                    triage_max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    triage_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    embeddings_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    embeddings_temperature = table.Column<double>(type: "double precision", nullable: true),
                    embeddings_max_output_tokens = table.Column<int>(type: "integer", nullable: true),
                    embeddings_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_model_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_reviews",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issue_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    repo_full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    pull_number = table.Column<int>(type: "integer", nullable: false),
                    pull_request_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    latest_iteration_id = table.Column<Guid>(type: "uuid", nullable: true),
                    iterations_count = table.Column<int>(type: "integer", nullable: false),
                    latest_verdict = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_reviews", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "context_documents",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    repo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    repo_branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    repo_commit_sha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    raw_content_size = table.Column<int>(type: "integer", nullable: false),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_context_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vcs_installations",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    installation_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    owner_login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    owner_external_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    linked_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    repo_selections = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vcs_installations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_review_iterations",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ai_review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    iteration_number = table.Column<int>(type: "integer", nullable: false),
                    commit_sha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    verdict = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: false),
                    inline_comments_count = table.Column<int>(type: "integer", nullable: false),
                    github_review_id = table.Column<long>(type: "bigint", nullable: true),
                    model_used = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: true),
                    output_tokens = table.Column<int>(type: "integer", nullable: true),
                    used_chunk_ids = table.Column<string>(type: "jsonb", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_review_iterations", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_review_iterations_ai_reviews_ai_review_id",
                        column: x => x.ai_review_id,
                        principalSchema: "assignment_review",
                        principalTable: "ai_reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "context_chunks",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_context_chunks", x => x.id);
                    table.ForeignKey(
                        name: "FK_context_chunks_context_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "assignment_review",
                        principalTable: "context_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_ai_review_iterations_review_number",
                schema: "assignment_review",
                table: "ai_review_iterations",
                columns: new[] { "ai_review_id", "iteration_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ai_reviews_user_id",
                schema: "assignment_review",
                table: "ai_reviews",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_ai_reviews_submission",
                schema: "assignment_review",
                table: "ai_reviews",
                column: "submission_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_context_chunks_document_id",
                schema: "assignment_review",
                table: "context_chunks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_context_documents_owner",
                schema: "assignment_review",
                table: "context_documents",
                columns: new[] { "owner_type", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_vcs_installations_linked_user_id",
                schema: "assignment_review",
                table: "vcs_installations",
                column: "linked_user_id");

            migrationBuilder.CreateIndex(
                name: "uq_vcs_installations_provider_installation",
                schema: "assignment_review",
                table: "vcs_installations",
                columns: new[] { "provider", "installation_id" },
                unique: true);

            // pgvector колонка для context_chunks. EF Core не знает про vector(N) тип,
            // добавляем через raw SQL. Extension `vector` уже создан init-databases.sql
            // (Phase 1). На Phase 6 будет Pgvector.EntityFrameworkCore adapter +
            // отдельная миграция NOT NULL constraint.
            //
            // Колонка nullable на Phase 2 (skeleton без inserts): pgvector не имеет
            // valid empty default ('[]'::vector → "vector must have at least 1 dimension"),
            // а seed-нуля в 1536 элементов в migration'е — code-smell. Phase 6 поднимет
            // NOT NULL после того как adapter научит EF писать embedding'и при insert'е.
            migrationBuilder.Sql(
                "ALTER TABLE assignment_review.context_chunks ADD COLUMN embedding vector(1536) NULL;");

            // HNSW индекс для cosine similarity queries (top-K retrieve в Phase 7
            // /run-iteration handler). vector_cosine_ops — operator class для расстояния
            // 1 - cosine_similarity. Параметры дефолтные (m=16, ef_construction=64) —
            // разумный baseline для ~100K chunks.
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ix_context_chunks_embedding_hnsw
                    ON assignment_review.context_chunks
                    USING hnsw (embedding vector_cosine_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS assignment_review.ix_context_chunks_embedding_hnsw;");

            migrationBuilder.DropTable(
                name: "ai_model_settings",
                schema: "assignment_review");

            migrationBuilder.DropTable(
                name: "ai_review_iterations",
                schema: "assignment_review");

            migrationBuilder.DropTable(
                name: "context_chunks",
                schema: "assignment_review");

            migrationBuilder.DropTable(
                name: "vcs_installations",
                schema: "assignment_review");

            migrationBuilder.DropTable(
                name: "ai_reviews",
                schema: "assignment_review");

            migrationBuilder.DropTable(
                name: "context_documents",
                schema: "assignment_review");
        }
    }
}
