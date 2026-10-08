-- Issue #358: collapse AccessType.FREE → REGISTERED (system-default free access).
-- Idempotent: WHERE access_type = 'FREE' is no-op after first run.
--
-- NB: после этого UPDATE downstream Redis-теги для затронутых ресурсов будут stale
-- (всё ещё содержат plan:free:author_X и т.п.) до запуска `resync-access-tags`.
-- Deploy-пайплайн должен звать `resync-access-tags` после `migrate-data`.
UPDATE education.materials   SET access_type = 'REGISTERED' WHERE access_type = 'FREE';
UPDATE education.issues      SET access_type = 'REGISTERED' WHERE access_type = 'FREE';
UPDATE education.collections SET access_type = 'REGISTERED' WHERE access_type = 'FREE';
