---
paths: ["backend/**/Migrations/**", "backend/**/DataMigrations/**"]
---

# Migration safety

Committed migrations are immutable history. Never edit, rename, reorder, or remove a migration that
may have run in a shared environment. Correct schema or data behavior with a new forward migration.

Before changing migrations, read the nearest service `AGENTS.md` and inspect its current migration
and integration-test setup. Verify a new migration against a fresh database and an upgraded
database using the affected service's integration path. Business failures use the project
Result/Error contract; migration failures remain deployment failures and must not be swallowed.

`check-migrations` blocks deleted migration files. Its `[skip-migrations-check]` commit marker is
reserved for an owner-approved removal of a whole service together with its schema, or a history
rewrite. Either is a separate operational task with an environment inventory, backups, and a
restore rehearsal; it is never embedded in feature delivery or used as a reusable escape hatch.
