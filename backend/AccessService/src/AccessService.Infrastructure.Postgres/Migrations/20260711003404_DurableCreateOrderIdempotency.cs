using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AccessService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class DurableCreateOrderIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.AlterColumn<string>(
                name: "response_body",
                schema: "access",
                table: "idempotency_keys",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<string>(
                name: "expected_scope",
                schema: "access",
                table: "idempotency_keys",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "order_id",
                schema: "access",
                table: "idempotency_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "plan_id",
                schema: "access",
                table: "idempotency_keys",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                schema: "access",
                table: "idempotency_keys",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "access",
                table: "idempotency_keys",
                type: "timestamp with time zone",
                nullable: true);

            // Legacy rows contain a serialized CreateOrderResponse. Parse each row separately
            // so one malformed legacy body cannot abort the migration; malformed/orphaned rows
            // are the only rows removed. Valid rows derive request identity from Order + Plan.
            migrationBuilder.Sql(
                """
                DO $backfill$
                DECLARE
                    legacy_row record;
                    parsed_order_id uuid;
                BEGIN
                    FOR legacy_row IN
                        SELECT key, user_id, response_body
                        FROM access.idempotency_keys
                    LOOP
                        BEGIN
                            parsed_order_id := NULLIF(
                                legacy_row.response_body::jsonb ->> 'OrderId', '')::uuid;
                        EXCEPTION WHEN OTHERS THEN
                            parsed_order_id := NULL;
                        END;

                        IF parsed_order_id IS NULL THEN
                            DELETE FROM access.idempotency_keys
                            WHERE key = legacy_row.key AND user_id = legacy_row.user_id;
                        ELSE
                            UPDATE access.idempotency_keys
                            SET order_id = parsed_order_id
                            WHERE key = legacy_row.key AND user_id = legacy_row.user_id;
                        END IF;
                    END LOOP;
                END
                $backfill$;

                UPDATE access.idempotency_keys AS key_state
                SET plan_id = orders.plan_id
                FROM access.orders AS orders
                WHERE orders.id = key_state.order_id;

                UPDATE access.idempotency_keys AS key_state
                SET expected_scope = plans.scope
                FROM access.plans AS plans
                WHERE plans.id = key_state.plan_id;

                DELETE FROM access.idempotency_keys
                WHERE order_id IS NULL OR plan_id IS NULL OR expected_scope IS NULL;

                UPDATE access.idempotency_keys
                SET status = 'COMPLETED',
                    updated_at = created_at;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "expected_scope",
                schema: "access",
                table: "idempotency_keys",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "order_id",
                schema: "access",
                table: "idempotency_keys",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "plan_id",
                schema: "access",
                table: "idempotency_keys",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "access",
                table: "idempotency_keys",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "access",
                table: "idempotency_keys",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "access",
                table: "idempotency_keys",
                columns: new[] { "user_id", "key" });

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_order_id",
                schema: "access",
                table: "idempotency_keys",
                column: "order_id");

            migrationBuilder.AddForeignKey(
                name: "FK_idempotency_keys_orders_order_id",
                schema: "access",
                table: "idempotency_keys",
                column: "order_id",
                principalSchema: "access",
                principalTable: "orders",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_idempotency_keys_orders_order_id",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.DropIndex(
                name: "IX_idempotency_keys_order_id",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.DropPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "expected_scope",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "order_id",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "plan_id",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "status",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "access",
                table: "idempotency_keys");

            migrationBuilder.AlterColumn<string>(
                name: "response_body",
                schema: "access",
                table: "idempotency_keys",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            // The old schema had a global key PK. After running the new schema, different
            // users may legitimately reuse the same raw key, so collapse duplicates before
            // restoring the legacy PK during rollback.
            migrationBuilder.Sql("""
                DELETE FROM access.idempotency_keys
                WHERE ctid IN (
                    SELECT duplicate.ctid
                    FROM (
                        SELECT ctid,
                               row_number() OVER (
                                   PARTITION BY key
                                   ORDER BY created_at, user_id) AS row_number
                        FROM access.idempotency_keys
                    ) AS duplicate
                    WHERE duplicate.row_number > 1
                );
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_idempotency_keys",
                schema: "access",
                table: "idempotency_keys",
                column: "key");
        }
    }
}
