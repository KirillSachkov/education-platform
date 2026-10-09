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

CREATE EXTENSION IF NOT EXISTS ltree;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS vector;
