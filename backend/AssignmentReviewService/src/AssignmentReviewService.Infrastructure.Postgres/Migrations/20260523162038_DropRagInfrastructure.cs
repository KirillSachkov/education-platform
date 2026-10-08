using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DropRagInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HNSW индекс на context_chunks.embedding vector(1536). Postgres
            // сносит индекс автоматически при DROP TABLE, но явный DROP INDEX
            // IF EXISTS делает миграцию идемпотентной — если в каком-то env'е
            // таблица уже была дропнута руками, миграция не упадёт.
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS assignment_review.ix_context_chunks_embedding_hnsw;");

            migrationBuilder.DropTable(
                name: "context_chunks",
                schema: "assignment_review");

            migrationBuilder.DropTable(
                name: "context_documents",
                schema: "assignment_review");

            migrationBuilder.DropColumn(
                name: "used_chunk_ids",
                schema: "assignment_review",
                table: "ai_review_iterations");

            migrationBuilder.DropColumn(
                name: "embeddings_max_output_tokens",
                schema: "assignment_review",
                table: "ai_model_settings");

            migrationBuilder.DropColumn(
                name: "embeddings_model",
                schema: "assignment_review",
                table: "ai_model_settings");

            migrationBuilder.DropColumn(
                name: "embeddings_temperature",
                schema: "assignment_review",
                table: "ai_model_settings");

            migrationBuilder.DropColumn(
                name: "embeddings_timeout_seconds",
                schema: "assignment_review",
                table: "ai_model_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ВНИМАНИЕ: lossy rollback. Восстанавливаем структуру таблиц и
            // embeddings-колонок ai_model_settings, но pgvector embedding-колонку
            // и HNSW индекс на context_chunks НЕ восстанавливаем. Это by design:
            // rollback'ом отдельно никогда не пользуемся — прод ARS HELD по #231
            // на момент выпила, а на dev таблицы были пустые. Если когда-нибудь
            // понадобится full-rollback — раскатить ещё одну миграцию, которая
            // дополнит структуру raw SQL'ом из оригинала 20260510112103_InitialCreate.
            migrationBuilder.AddColumn<string>(
                name: "used_chunk_ids",
                schema: "assignment_review",
                table: "ai_review_iterations",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "embeddings_max_output_tokens",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "embeddings_model",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<double>(
                name: "embeddings_temperature",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "embeddings_timeout_seconds",
                schema: "assignment_review",
                table: "ai_model_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "context_documents",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    raw_content_size = table.Column<int>(type: "integer", nullable: false),
                    repo_branch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    repo_commit_sha = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    repo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_context_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "context_chunks",
                schema: "assignment_review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chunk_index = table.Column<int>(type: "integer", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    token_count = table.Column<int>(type: "integer", nullable: false)
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
                name: "ix_context_chunks_document_id",
                schema: "assignment_review",
                table: "context_chunks",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_context_documents_owner",
                schema: "assignment_review",
                table: "context_documents",
                columns: new[] { "owner_type", "owner_id" });
        }
    }
}
