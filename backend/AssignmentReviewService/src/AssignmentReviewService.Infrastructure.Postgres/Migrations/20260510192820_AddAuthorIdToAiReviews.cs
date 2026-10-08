using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssignmentReviewService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthorIdToAiReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "author_id",
                schema: "assignment_review",
                table: "ai_reviews",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "author_id",
                schema: "assignment_review",
                table: "ai_reviews");
        }
    }
}
