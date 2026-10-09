# Operations: Config, Secrets, CI/CD, Email, Backup

Operational concerns не нужные на каждой задаче. Если меняешь CI/CD пайплайны или процедуры
релиза — смотри также [`docs/agents/release-pipelines.md`](agents/release-pipelines.md).

## Configuration Architecture

**Three layers** (highest priority wins):
1. Environment variables (from `.env` / Infisical)
2. `appsettings.{Environment}.json` per service
3. `appsettings.json` base config

**What goes where:**
- **Secrets** (passwords, API keys, signing keys) → `.env` (dev) / Infisical (prod)
- **Non-secret config** (URLs, CORS, client IDs, feature flags) → `appsettings.{Environment}.json`
- **Business constants** (gamification levels, retention TTLs) → `appsettings.json`

**Environments:** `Development` (local `dotnet run`), `Docker` (dev compose), `Production` (prod server).

## Secret Management

**Development:** `cp .env.example .env` — contains dev-safe defaults (postgres/postgres, guest/guest).

**Production:** Self-hosted Infisical on VPS. CI deploy exports secrets via `infisical export` to `.env` on server. Access Infisical UI via SSH tunnel (`ssh -L 8080:localhost:8080 root@server`).

Only secrets belong in Infisical/`.env`. Non-secret production config is in `appsettings.Production.json` per service.

## CI/CD Pipeline

GitHub Actions runs affected PR checks and the required aggregate. Trusted main builds thirteen
application images and an immutable digest manifest in GHCR. Standard hosted runners and bounded
artifact retention are configured in `.github/workflows/ci.yml`.

Production operations use the separate manual `.github/workflows/production.yml` on trusted main.
An owner command selects the exact release; PRs and main builds do not deploy. The workflow uses
independent SSH, verifies a private backup before migrations, checks health and image digests,
then promotes release metadata. See [`docs/agents/release-pipelines.md`](agents/release-pipelines.md)
and [`docs/github-production.md`](github-production.md) for release inputs and recovery behavior.

`backend/nuget.config.ci` selects public NuGet sources. `docker-compose.prod.yml` has no build
sections and requires the audited PostgreSQL image in `RESTORE_POSTGRES_IMAGE`. Production
operations retain the approved immutable database digest and volume.

## Email

**Dev:** Mailpit (SMTP localhost:1025, Web UI http://localhost:8025). Uses `SmtpEmailSender`.

**Prod:** Unisender Go HTTP API (`go2.unisender.ru`). Uses `UnisenderEmailSender`. API key via `EMAIL__APIKEY` env var. SMTP ports are blocked by the VPS provider — only HTTP API works.

Sender selection is automatic: if `EmailOptions.ApiKey` is set, HTTP API is used; otherwise SMTP.

## Backup

**Dev:** `./scripts/dev.sh backup` — local gzipped pg_dumpall, last 10 retained.

**Prod:** `scripts/backup-s3.sh` — pg_dumpall → gzip → Yandex Object Storage (bucket from `BACKUP_S3_BUCKET`). Cron daily at 3:00 UTC, 30-day retention. Pre-deploy backups also created by CI pipeline.

Credentials: `BACKUP_S3_ACCESS_KEY` / `BACKUP_S3_SECRET_KEY` env vars.

### Production restore drill

Run the latest backup through an isolated restore without touching production PostgreSQL:

```bash
cd /opt/education-platform
# Supply BACKUP_S3_ACCESS_KEY and BACKUP_S3_SECRET_KEY from your secret manager.
# Use the approved repository@sha256 digest; this placeholder is not executable.
export RESTORE_POSTGRES_IMAGE='<approved PostgreSQL image>'
./scripts/restore-s3.sh --drill --latest
```

An explicit bucket-relative object key can be checked instead of `--latest`:

```bash
./scripts/restore-s3.sh --drill backups/backup_20260716_030000.sql.gz
```

The script refuses to start without `--drill`. It downloads and validates the gzip archive,
restores it with `ON_ERROR_STOP=1` into a disposable PostgreSQL 16 + pgvector container with no
network, handles the known `pg_monitor` grant incompatibility, and verifies all application
schemas plus non-empty control tables. The container, volume, and temporary archive are removed
after the check. Use `--keep` only while diagnosing a failed drill.

This command does not replace the live database. A real disaster recovery operation must first
stop application writes, preserve the current state, validate the exact chosen archive with this
drill, and only then restore it under incident control.

### Private GitLab archive recovery

Historical GitLab data, repository history, uploads, registry contents and matching configuration
remain in encrypted private recovery archives. The private recovery index pins object versions,
checksums, GitLab CE version, expected counts and the age identity location. Archive restore uses
an isolated disposable instance, matching secrets and edition, and no production volumes or ports.

`scripts/gitlab-restore-drill.sh` and `ops/gitlab/` support archive recovery. They are outside the
platform CI and production release path. Read the private recovery index before restoring an
archive; confirm its full checksum and exact version before creating disposable resources.

Полные восстановительные процедуры (потеря Redis, дрейф grants, stuck Wolverine envelopes
и т.д.) — в [`docs/RUNBOOK.md`](RUNBOOK.md).
