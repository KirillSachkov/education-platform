using Microsoft.EntityFrameworkCore;

namespace SearchService.IntegrationTests.Infrastructure;

internal static class WolverineSchemaHelper
{
    public static async Task CreateTablesAsync(DbContext dbContext)
    {
        const string sql = @"
            CREATE SCHEMA IF NOT EXISTS search;

            CREATE TABLE IF NOT EXISTS search.wolverine_outgoing_envelopes (
                id              uuid                        NOT NULL,
                owner_id        integer                     NOT NULL,
                destination     varchar                     NOT NULL,
                deliver_by      timestamp with time zone    NULL,
                body            bytea                       NOT NULL,
                attempts        integer                     NULL DEFAULT 0,
                message_type    varchar                     NOT NULL,
                CONSTRAINT pkey_wolverine_outgoing_envelopes_id PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS search.wolverine_incoming_envelopes (
                id                uuid                        NOT NULL,
                status            varchar                     NOT NULL,
                owner_id          integer                     NOT NULL,
                execution_time    timestamp with time zone    NULL DEFAULT NULL,
                attempts          integer                     NULL DEFAULT 0,
                body              bytea                       NOT NULL,
                message_type      varchar                     NOT NULL,
                received_at       varchar                     NULL,
                keep_until        timestamp with time zone    NULL,
                CONSTRAINT pkey_wolverine_incoming_envelopes_id PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS search.wolverine_dead_letters (
                id                   uuid                        NOT NULL,
                execution_time       timestamp with time zone    NULL DEFAULT NULL,
                body                 bytea                       NOT NULL,
                message_type         varchar                     NOT NULL,
                received_at          varchar                     NULL,
                source               varchar                     NULL,
                exception_type       varchar                     NULL,
                exception_message    varchar                     NULL,
                sent_at              timestamp with time zone    NULL,
                replayable           boolean                     NULL,
                CONSTRAINT pkey_wolverine_dead_letters_id PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS search.wolverine_nodes (
                id              uuid                        NOT NULL,
                node_number     serial                      NOT NULL,
                description     varchar                     NOT NULL,
                uri             varchar                     NOT NULL,
                started         timestamp with time zone    NOT NULL DEFAULT now(),
                health_check    timestamp with time zone    NOT NULL DEFAULT now(),
                version         varchar                     NULL,
                capabilities    text[]                      NULL,
                CONSTRAINT pkey_wolverine_nodes_id PRIMARY KEY (id)
            );

            CREATE TABLE IF NOT EXISTS search.wolverine_node_assignments (
                id         varchar                     NOT NULL,
                node_id    uuid                        NULL,
                started    timestamp with time zone    NOT NULL DEFAULT now(),
                CONSTRAINT pkey_wolverine_node_assignments_id PRIMARY KEY (id)
            );
        ";

        await dbContext.Database.ExecuteSqlRawAsync(sql);
    }
}
