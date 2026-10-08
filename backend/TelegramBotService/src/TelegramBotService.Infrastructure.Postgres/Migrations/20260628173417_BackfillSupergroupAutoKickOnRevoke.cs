using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBotService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSupergroupAutoKickOnRevoke : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #687: course GROUP (SUPERGROUP) bindings now auto-kick on access revoke/expire by
            // default. Flip already-bound course groups so existing communities start kicking
            // expired members reliably. CHANNEL bindings are left untouched (channel kicks are
            // unsupported / best-effort). Idempotent: re-running only touches rows still false.
            migrationBuilder.Sql(
                """
                UPDATE telegrambot.chat_bindings
                SET auto_kick_on_revoke = TRUE
                WHERE chat_type = 'SUPERGROUP'
                  AND auto_kick_on_revoke = FALSE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data backfill — not reversible (we can't recover which SUPERGROUP bindings were
            // originally opt-out). No-op down.
        }
    }
}
