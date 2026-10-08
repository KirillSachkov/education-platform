using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class CleanupUnconfirmedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Clean up garbage accounts created before email verification was enforced.
                -- These are users who registered but never confirmed their email via OTP.

                DELETE FROM auth.user_tokens WHERE "UserId" IN (
                    SELECT "Id" FROM auth.users WHERE "EmailConfirmed" = false
                );
                DELETE FROM auth.user_roles WHERE "UserId" IN (
                    SELECT "Id" FROM auth.users WHERE "EmailConfirmed" = false
                );
                DELETE FROM auth.user_logins WHERE "UserId" IN (
                    SELECT "Id" FROM auth.users WHERE "EmailConfirmed" = false
                );
                DELETE FROM auth.user_claims WHERE "UserId" IN (
                    SELECT "Id" FROM auth.users WHERE "EmailConfirmed" = false
                );
                DELETE FROM auth.user_profiles WHERE id IN (
                    SELECT "Id" FROM auth.users WHERE "EmailConfirmed" = false
                );
                DELETE FROM auth.users WHERE "EmailConfirmed" = false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — deleted rows cannot be restored
        }
    }
}
