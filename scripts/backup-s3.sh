#!/usr/bin/env bash
set -euo pipefail

# ─── Database backup to Yandex Object Storage (S3) ──────────────────────────
#
# Usage:
#   BACKUP_S3_ACCESS_KEY=... BACKUP_S3_SECRET_KEY=... ./scripts/backup-s3.sh
#
# Or with .env (POSIX-compatible — works under cron/dash):
#   set -a && . ./.env && set +a && ./scripts/backup-s3.sh
#
# Install aws cli (one-time):
#   apt install -y awscli
#
# Crontab entry (note: cron uses /bin/sh = dash on Ubuntu, so set SHELL=/bin/bash
# or use `.` instead of `source`):
#   SHELL=/bin/bash
#   PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
#   0 3 * * * cd /opt/education-platform && set -a && . /opt/education-platform/.env && set +a && /opt/education-platform/scripts/backup-s3.sh >> /var/log/backup-s3.log 2>&1
# ─────────────────────────────────────────────────────────────────────────────

S3_BUCKET="${BACKUP_S3_BUCKET:-education-platform-backups}"
S3_ENDPOINT="${BACKUP_S3_ENDPOINT:-https://storage.yandexcloud.net}"
S3_ACCESS_KEY="${BACKUP_S3_ACCESS_KEY:-}"
S3_SECRET_KEY="${BACKUP_S3_SECRET_KEY:-}"
RETENTION_DAYS="${BACKUP_S3_RETENTION_DAYS:-30}"

PG_CONTAINER="${BACKUP_PG_CONTAINER:-postgres}"
PG_USER="${POSTGRES_USER:-platform}"
PG_PASSWORD="${POSTGRES_PASSWORD:-}"

TIMESTAMP=$(date +%Y%m%d_%H%M%S)
BACKUP_FILE="backup_${TIMESTAMP}.sql.gz"
TMP_DIR="/tmp/pg-backups"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S UTC')] $*"; }
log_err() { echo "[$(date '+%Y-%m-%d %H:%M:%S UTC')] ERROR: $*" >&2; }

# ─── Validation ──────────────────────────────────────────────────────────────
if [[ -z "$S3_ACCESS_KEY" || -z "$S3_SECRET_KEY" ]]; then
  log_err "BACKUP_S3_ACCESS_KEY and BACKUP_S3_SECRET_KEY must be set"
  exit 1
fi

if ! command -v aws &>/dev/null; then
  log_err "aws cli not found. Install: apt install -y awscli"
  exit 1
fi

if ! docker ps --format '{{.Names}}' | grep -q "^${PG_CONTAINER}$"; then
  log_err "Container '${PG_CONTAINER}' is not running"
  exit 1
fi

export AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY"
export AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY"
export AWS_DEFAULT_REGION="ru-central1"

# ─── Dump ────────────────────────────────────────────────────────────────────
mkdir -p "$TMP_DIR"
log "Starting pg_dumpall (user=${PG_USER}, container=${PG_CONTAINER})..."

PGPASSWORD_ENV=""
if [[ -n "$PG_PASSWORD" ]]; then
  PGPASSWORD_ENV="-e PGPASSWORD=${PG_PASSWORD}"
fi

if ! docker exec $PGPASSWORD_ENV "$PG_CONTAINER" \
    pg_dumpall -U "$PG_USER" --clean --if-exists \
    | gzip > "$TMP_DIR/$BACKUP_FILE"; then
  log_err "pg_dumpall failed"
  rm -f "$TMP_DIR/$BACKUP_FILE"
  exit 1
fi

SIZE=$(du -h "$TMP_DIR/$BACKUP_FILE" | cut -f1)
log "Dump complete: $BACKUP_FILE ($SIZE)"

# Check dump is not empty/corrupt (min 10KB expected)
BYTES=$(stat -c%s "$TMP_DIR/$BACKUP_FILE")
if [[ "$BYTES" -lt 10240 ]]; then
  log_err "Dump suspiciously small (${BYTES} bytes) — aborting upload"
  rm -f "$TMP_DIR/$BACKUP_FILE"
  exit 1
fi

# ─── Upload ──────────────────────────────────────────────────────────────────
log "Uploading to s3://${S3_BUCKET}/${BACKUP_FILE}..."

if ! aws s3 cp "$TMP_DIR/$BACKUP_FILE" "s3://${S3_BUCKET}/${BACKUP_FILE}" \
    --endpoint-url "$S3_ENDPOINT" \
    --quiet; then
  log_err "S3 upload failed"
  rm -f "$TMP_DIR/$BACKUP_FILE"
  exit 1
fi

log "Upload complete"
rm -f "$TMP_DIR/$BACKUP_FILE"

# ─── Cleanup old S3 backups ──────────────────────────────────────────────────
log "Cleaning up S3 backups older than ${RETENTION_DAYS} days..."

CUTOFF=$(date -u -d "${RETENTION_DAYS} days ago" +%Y%m%d 2>/dev/null || echo "")

if [[ -n "$CUTOFF" ]]; then
  aws s3 ls "s3://${S3_BUCKET}/" --endpoint-url "$S3_ENDPOINT" 2>/dev/null \
    | while read -r line; do
      file_name=$(echo "$line" | awk '{print $4}')
      file_date=$(echo "$file_name" | grep -oP 'backup_\K\d{8}' || true)
      if [[ -n "$file_date" && "$file_date" < "$CUTOFF" ]]; then
        log "  Deleting old backup: $file_name"
        aws s3 rm "s3://${S3_BUCKET}/${file_name}" --endpoint-url "$S3_ENDPOINT" --quiet
      fi
    done
fi

log "Done. Backup successful: s3://${S3_BUCKET}/${BACKUP_FILE}"
