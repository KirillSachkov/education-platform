using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramBotService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RebindChatsAndDecisionsToPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "course_id",
                schema: "telegrambot",
                table: "chat_bindings",
                newName: "plan_id");

            migrationBuilder.RenameIndex(
                name: "ux_chat_bindings_course_chat",
                schema: "telegrambot",
                table: "chat_bindings",
                newName: "ux_chat_bindings_plan_chat");

            migrationBuilder.RenameIndex(
                name: "ix_chat_bindings_course",
                schema: "telegrambot",
                table: "chat_bindings",
                newName: "ix_chat_bindings_plan");

            migrationBuilder.RenameColumn(
                name: "course_id",
                schema: "telegrambot",
                table: "bot_decisions",
                newName: "plan_id");

            // Backfill: chat_bindings.plan_id сейчас содержит старые course_id (rename column
            // не переписал значения). Меняем их на LIFETIME_ALL plan_id автора этого курса.
            // Cross-schema lookup education.courses → access.plans. Если LIFETIME_ALL plan
            // у автора отсутствует — binding осиротеет (plan_id = guid несуществующего плана):
            // F1/F6 не сработают, admin восстановит вручную через /telegram/admin/plans/{X}/chat-bindings/.
            //
            // IF EXISTS guards: тестовая БД содержит только telegrambot schema, education и
            // access схем нет — миграция должна молча пропускать backfill в test environment.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'education')
                       AND EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'access')
                    THEN
                        -- Pre-UPDATE дедуп: до того как N course'ов одного автора замэпятся
                        -- в один LIFETIME_ALL план, удаляем дубликаты по (author, chat) —
                        -- иначе UPDATE row-by-row отобьётся на UNIQUE(plan_id, chat_id) при
                        -- second row. plan_id ещё содержит старый course_id, поэтому JOIN
                        -- идёт через education.courses → author_id. Оставляем самый старый row.
                        DELETE FROM telegrambot.chat_bindings cb
                        USING education.courses c
                        WHERE cb.plan_id = c.id
                          AND EXISTS (
                              SELECT 1
                              FROM telegrambot.chat_bindings older
                              JOIN education.courses oc ON oc.id = older.plan_id
                              WHERE oc.author_id = c.author_id
                                AND older.telegram_chat_id = cb.telegram_chat_id
                                AND older.created_at < cb.created_at
                          );

                        UPDATE telegrambot.chat_bindings cb
                        SET plan_id = p.id
                        FROM education.courses c
                        JOIN access.plans p ON p.author_id = c.author_id
                            AND p.kind = 'LIFETIME_ALL'
                            AND p.archived_at IS NULL
                        WHERE cb.plan_id = c.id;

                        -- Surface orphans: bindings с plan_id, который не существует в access.plans
                        -- (автор не имеет LIFETIME_ALL plan'а). Для них F1/F6 silently не сработают.
                        RAISE NOTICE 'Orphaned chat_bindings after backfill: %',
                            (SELECT count(*) FROM telegrambot.chat_bindings cb
                             WHERE NOT EXISTS (SELECT 1 FROM access.plans p WHERE p.id = cb.plan_id));
                    END IF;
                END
                $$;
            """);

            // bot_decisions.plan_id — audit, нет UNIQUE constraint и нет каскадных эффектов.
            // Старые row'ы с course_id (теперь под именем plan_id) остаются исторически — не трогаем.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "plan_id",
                schema: "telegrambot",
                table: "chat_bindings",
                newName: "course_id");

            migrationBuilder.RenameIndex(
                name: "ux_chat_bindings_plan_chat",
                schema: "telegrambot",
                table: "chat_bindings",
                newName: "ux_chat_bindings_course_chat");

            migrationBuilder.RenameIndex(
                name: "ix_chat_bindings_plan",
                schema: "telegrambot",
                table: "chat_bindings",
                newName: "ix_chat_bindings_course");

            migrationBuilder.RenameColumn(
                name: "plan_id",
                schema: "telegrambot",
                table: "bot_decisions",
                newName: "course_id");
        }
    }
}
