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

Self-hosted GitLab at `https://gitlab-sachkov.ru`. Registry: `gitlab-sachkov.ru:5050`.

### Pipeline stages

The authority is [`docs/agents/release-pipelines.md`](agents/release-pipelines.md); in short:

```
MR to main (test stage, affected-only by changes:):
  mr-main-gate, unit-tests, build-frontend, check-migrations, contract and harness jobs
  integration-tests:all — optional manual full matrix

push to main:
  build-{service}         — Docker build + push of every service image
  prepare-release-images  — promote images to the immutable commit tag
  deploy-production       — MANUAL, run only on an owner command; deploys IMAGE_TAG=$CI_COMMIT_SHA
  tag-release             — after deploy, tags the top released CHANGELOG version
  rollback-production     — MANUAL, owner-authorized
```

### Key CI files
- `.gitlab-ci.yml` — pipeline definition
- `backend/nuget.config.ci` — public NuGet source configuration for CI
- `docker-compose.prod.yml` — production compose (no `build:` sections, images from registry)

### Release modes

Один путь — подробно в [`docs/agents/release-pipelines.md`](agents/release-pipelines.md).
Кратко: `короткая ветка от main → MR в main → merge владельцем → сборка образов на main →
ручной deploy-production по команде владельца`. Срочный фикс идёт тем же путём с минимальным diff.

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
set -a && . ./.env && set +a
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

### Self-hosted GitLab backup

The GitLab host uses `gitlab-data-backup.timer` every day at 02:15 UTC. The wrapper creates an
Omnibus backup with `registry,remote` skipped, validates the tar structure and database gzip,
uploads it to `s3://example-private-backups/data/`, and confirms the remote size. An old local
archive is removed only when its matching offsite object passes the same checks. GitLab's own
`backup_keep_time` must remain `0`, because retention belongs to this verified wrapper.

`gitlab-restore-drill.timer` downloads and validates the freshest offsite archive every Sunday at
05:30 UTC. Both units invoke `gitlab-backup-alert@.service` on failure. Its populated environment
file lives only on the host at `/etc/gitlab-backup/alert.env` with owner `root:root` and mode `600`.

Useful checks on the GitLab host:

```bash
systemctl list-timers gitlab-data-backup.timer gitlab-restore-drill.timer
systemctl start gitlab-data-backup.service
systemctl start gitlab-restore-drill.service
journalctl -u gitlab-data-backup.service -u gitlab-restore-drill.service
```

The full restore mode accepts only an explicitly named, labelled disposable container with an
exact GitLab version and edition match. Current production backups are GitLab CE:

```bash
export GITLAB_RESTORE_LABEL=com.sachkov.gitlab-restore-drill=true
docker volume create --label "$GITLAB_RESTORE_LABEL" gitlab-restore-config
docker volume create --label "$GITLAB_RESTORE_LABEL" gitlab-restore-logs
docker volume create --label "$GITLAB_RESTORE_LABEL" gitlab-restore-data

docker run -d \
  --name gitlab-restore-drill \
  --hostname gitlab-restore-drill \
  --label "$GITLAB_RESTORE_LABEL" \
  --shm-size 256m \
  -v gitlab-restore-config:/etc/gitlab \
  -v gitlab-restore-logs:/var/log/gitlab \
  -v gitlab-restore-data:/var/opt/gitlab \
  gitlab/gitlab-ce:18.10.1-ce.0

# Decrypt the recovery archive only on this trusted host, then install its
# matching gitlab-secrets.json into the disposable config volume.
docker cp /secure/recovery/gitlab-secrets.json \
  gitlab-restore-drill:/etc/gitlab/gitlab-secrets.json
docker exec -u 0 gitlab-restore-drill \
  sh -c 'chown root:root /etc/gitlab/gitlab-secrets.json && chmod 600 /etc/gitlab/gitlab-secrets.json'
docker exec gitlab-restore-drill gitlab-ctl reconfigure

./scripts/gitlab-restore-drill.sh --execute \
  --target-container gitlab-restore-drill \
  --confirm-target gitlab-restore-drill \
  --source-edition ce
```

Do not use bind mounts for `/etc/gitlab`, `/var/log/gitlab`, or `/var/opt/gitlab`, especially not
the live host paths. The script requires all three named volumes to carry the restore-drill label,
refuses a volume shared with any other container, and refuses known live container names. Remove
the decrypted recovery file and disposable container/volumes after the drill.

Полные восстановительные процедуры (потеря Redis, дрейф grants, stuck Wolverine envelopes
и т.д.) — в [`docs/RUNBOOK.md`](RUNBOOK.md).
