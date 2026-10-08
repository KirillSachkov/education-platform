-- Создание баз данных при первом запуске PostgreSQL
-- Этот скрипт выполняется автоматически из /docker-entrypoint-initdb.d/

-- Единая БД платформы (schema-per-service)
CREATE DATABASE education_platform;

-- Схемы для каждого микросервиса + расширения
\c education_platform

CREATE SCHEMA IF NOT EXISTS access;
CREATE SCHEMA IF NOT EXISTS assignment_review;
CREATE SCHEMA IF NOT EXISTS auth;
CREATE SCHEMA IF NOT EXISTS comments;
CREATE SCHEMA IF NOT EXISTS education;
CREATE SCHEMA IF NOT EXISTS files;
CREATE SCHEMA IF NOT EXISTS notifications;
CREATE SCHEMA IF NOT EXISTS progress;
CREATE SCHEMA IF NOT EXISTS tags;
CREATE SCHEMA IF NOT EXISTS telegrambot;
CREATE SCHEMA IF NOT EXISTS trainer;

CREATE EXTENSION IF NOT EXISTS ltree;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS vector;

-- Grafana least-privilege read-only role for business dashboards (Trainer stats
-- Postgres datasource). SELECT on the `trainer` schema only — NEVER the app
-- superuser. Dev uses a literal non-secret password ('grafana_ro'); prod creates
-- this role with a real password from Infisical (owner-action) and passes
-- GRAFANA_PG_USER / GRAFANA_PG_PASSWORD into the grafana container.
-- Idempotent: this file runs only on a fresh volume, but the role create is guarded.
DO
$$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = 'grafana_ro') THEN
        CREATE ROLE grafana_ro LOGIN PASSWORD 'grafana_ro';
    END IF;
END
$$;

GRANT CONNECT ON DATABASE education_platform TO grafana_ro;
GRANT USAGE ON SCHEMA trainer TO grafana_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA trainer TO grafana_ro;
-- Trainer tables are created later by the migration container (as the app user);
-- default privileges make those future tables readable by grafana_ro automatically.
ALTER DEFAULT PRIVILEGES IN SCHEMA trainer GRANT SELECT ON TABLES TO grafana_ro;
