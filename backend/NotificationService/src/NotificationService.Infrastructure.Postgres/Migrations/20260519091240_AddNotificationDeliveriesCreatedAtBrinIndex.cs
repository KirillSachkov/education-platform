using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotificationService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveriesCreatedAtBrinIndex : Migration
    {
        // Issue #236, DB-3 — BRIN index на append-mostly time-series column.
        // BRIN-индекс ~1 страницу overhead vs B-tree, оптимизирует BETWEEN-range scans
        // в admin stats endpoints (GetDeliveryStats / ListDeliveries). Pages_per_range=32
        // — дефолт PG; для notification_deliveries c sub-second insert rate это даёт
        // достаточно мелкую гранулярность. EF Core HasIndex не умеет USING BRIN, поэтому
        // через raw SQL.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_notification_deliveries_created_at_brin
                ON notifications.notification_deliveries
                USING BRIN (created_at)
                WITH (pages_per_range = 32);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS notifications.ix_notification_deliveries_created_at_brin;");
        }
    }
}
