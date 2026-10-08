#!/bin/bash
# Создаёт read-only роль postgres_exporter для метрик и включает slow query log.
# Запускается один раз при инициализации тома postgres.
#
# Пароль берётся из env POSTGRES_EXPORTER_PASSWORD (см. .env).
# Если пуст — fallback на 'exporter' (только для dev).
set -euo pipefail

PASSWORD="${POSTGRES_EXPORTER_PASSWORD:-exporter}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-EOSQL
    DO \$\$
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'postgres_exporter') THEN
            CREATE USER postgres_exporter WITH PASSWORD '${PASSWORD}';
        END IF;
    END
    \$\$;
    GRANT pg_monitor TO postgres_exporter;
    GRANT CONNECT ON DATABASE education_platform TO postgres_exporter;

    -- Slow query log: всё, что > 500ms — в stdout (его подберёт Alloy/Promtail).
    -- Полезно для диагностики, не шумит на «тёплой» базе.
    ALTER SYSTEM SET log_min_duration_statement = 500;
    ALTER SYSTEM SET log_lock_waits = on;
    ALTER SYSTEM SET log_temp_files = 10485760;     -- 10MB
    ALTER SYSTEM SET log_checkpoints = on;
    ALTER SYSTEM SET log_line_prefix = '%t [%p]: db=%d,user=%u,app=%a,client=%h ';
    SELECT pg_reload_conf();
EOSQL
