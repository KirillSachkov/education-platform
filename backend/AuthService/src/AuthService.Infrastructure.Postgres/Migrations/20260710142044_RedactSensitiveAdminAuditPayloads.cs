using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RedactSensitiveAdminAuditPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION pg_temp.redact_sensitive_jsonb(input jsonb)
                RETURNS jsonb
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    result jsonb;
                    entry record;
                BEGIN
                    CASE jsonb_typeof(input)
                        WHEN 'object' THEN
                            result := '{}'::jsonb;
                            FOR entry IN SELECT key, value FROM jsonb_each(input)
                            LOOP
                                IF entry.key ~* '(password|token|secret|apikey|authorization)' THEN
                                    result := result || jsonb_build_object(entry.key, '[REDACTED]');
                                ELSE
                                    result := result || jsonb_build_object(
                                        entry.key,
                                        pg_temp.redact_sensitive_jsonb(entry.value));
                                END IF;
                            END LOOP;
                            RETURN result;
                        WHEN 'array' THEN
                            SELECT COALESCE(
                                jsonb_agg(
                                    pg_temp.redact_sensitive_jsonb(value)
                                    ORDER BY ord),
                                '[]'::jsonb)
                            INTO result
                            FROM jsonb_array_elements(input) WITH ORDINALITY AS a(value, ord);
                            RETURN result;
                        ELSE
                            RETURN input;
                    END CASE;
                END;
                $function$;

                UPDATE auth.admin_audit_log
                SET payload_json = pg_temp.redact_sensitive_jsonb(payload_json)
                WHERE payload_json IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Redacted credentials cannot and must not be restored.
        }
    }
}
