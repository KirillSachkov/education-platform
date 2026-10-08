using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CommentService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class EnforceApplicationGeneratedCommentIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Before enforcing the domain invariant, remove legacy cross-target reply
            // subtrees. Those rows could be created when a client supplied a parent from
            // one target and an accessible EntityReference from another target.
            migrationBuilder.Sql(
                """
                WITH invalid_roots AS (
                    SELECT child.path
                    FROM comments.comments child
                    JOIN comments.comments parent
                      ON parent.id = (subpath(child.path, nlevel(child.path) - 2, 1))::text::uuid
                    WHERE child.depth > 0
                      AND (
                          child.target_entity_type <> parent.target_entity_type
                          OR child.target_entity_id <> parent.target_entity_id
                      )
                )
                DELETE FROM comments.comments candidate
                USING invalid_roots invalid
                WHERE candidate.path <@ invalid.path;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "comments",
                table: "comments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "gen_random_uuid()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "id",
                schema: "comments",
                table: "comments",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }
    }
}