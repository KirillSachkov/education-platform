#!/usr/bin/env bash
set -euo pipefail
umask 077

# ─── Database backup to Yandex Object Storage (S3) ──────────────────────────
#
# Production entrypoint (also used by the daily 03:00 UTC cron):
#   python3 /opt/education-platform/scripts/ops/production/daily_backup.py
#
# The wrapper reads the protected environment without evaluating shell code and
# verifies encrypted runtime recovery before calling this database backup helper.
# This helper accepts BACKUP_S3_ACCESS_KEY / BACKUP_S3_SECRET_KEY in its environment.
# Install aws cli once: apt install -y awscli
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
TMP_DIR="${BACKUP_TMP_DIR:-/tmp/pg-backups}"

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
python3 - "$TMP_DIR" <<'PY_PRIVATE_DIRECTORY'
import os, stat, sys
from pathlib import Path
path = Path(sys.argv[1]); metadata = path.lstat()
if not stat.S_ISDIR(metadata.st_mode) or metadata.st_uid != os.geteuid():
    raise SystemExit("Backup temporary directory must be owned by the backup user")
path.chmod(0o700)
PY_PRIVATE_DIRECTORY
log "Starting pg_dumpall (user=${PG_USER}, container=${PG_CONTAINER})..."

export PGPASSWORD="$PG_PASSWORD"

if ! docker exec -e PGPASSWORD "$PG_CONTAINER" \
    pg_dumpall -U "$PG_USER" --clean --if-exists \
    | gzip > "$TMP_DIR/$BACKUP_FILE"; then
  log_err "pg_dumpall failed; partial dump retained"
  exit 1
fi

SIZE=$(du -h "$TMP_DIR/$BACKUP_FILE" | cut -f1)
log "Dump complete: $BACKUP_FILE ($SIZE)"

# Check dump is not empty/corrupt (min 10KB expected)
BYTES=$(stat -c%s "$TMP_DIR/$BACKUP_FILE")
if [[ "$BYTES" -lt 10240 ]]; then
  log_err "Dump suspiciously small (${BYTES} bytes) — aborting upload; dump retained"
  exit 1
fi

gzip -t "$TMP_DIR/$BACKUP_FILE"
CHECKSUM="$(sha256sum "$TMP_DIR/$BACKUP_FILE" | awk '{print $1}')"

# ─── Upload ──────────────────────────────────────────────────────────────────
log "Uploading to s3://${S3_BUCKET}/${BACKUP_FILE}..."

if ! aws s3 cp "$TMP_DIR/$BACKUP_FILE" "s3://${S3_BUCKET}/${BACKUP_FILE}" \
    --endpoint-url "$S3_ENDPOINT" \
    --acl private --metadata "sha256=$CHECKSUM" \
    --quiet; then
  log_err "S3 upload failed; local dump retained"
  exit 1
fi

# Verify the remote object before recording a successful backup or removing the dump.
HEAD_FILE="$(mktemp)"
if ! aws s3api head-object --bucket "$S3_BUCKET" --key "$BACKUP_FILE" \
    --endpoint-url "$S3_ENDPOINT" > "$HEAD_FILE"; then
  rm -f "$HEAD_FILE"
  log_err "Backup read-back failed; local dump retained"
  exit 1
fi
python3 - "$HEAD_FILE" "$BYTES" "$CHECKSUM" "${BACKUP_RECEIPT_FILE:-/var/lib/education-platform/last-backup.json}" <<'PY_RECEIPT'
import json, os, sys, tempfile, time
from pathlib import Path
head = json.loads(Path(sys.argv[1]).read_text())
checksums = [value for key, value in head.get("Metadata", {}).items() if key.lower() == "sha256"]
if head.get("ContentLength") != int(sys.argv[2]) or checksums != [sys.argv[3]]:
    raise SystemExit("Backup remote size or checksum metadata mismatch; local dump retained")
receipt = Path(sys.argv[4])
receipt.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
fd, temporary = tempfile.mkstemp(prefix=".backup-", dir=receipt.parent)
try:
    with os.fdopen(fd, "w") as stream:
        json.dump({"verified_at": time.time(), "size_bytes": int(sys.argv[2]), "sha256": sys.argv[3], "version_id": head.get("VersionId")}, stream)
        stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, receipt)
finally:
    if os.path.exists(temporary): os.unlink(temporary)
PY_RECEIPT
rm -f "$HEAD_FILE"
log "Upload and remote read-back complete"
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
