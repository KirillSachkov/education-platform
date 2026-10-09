#!/usr/bin/env bash

set -Eeuo pipefail

S3_BUCKET="${BACKUP_S3_BUCKET:-education-platform-backups}"
S3_ENDPOINT="${BACKUP_S3_ENDPOINT:-https://storage.yandexcloud.net}"
S3_ACCESS_KEY="${BACKUP_S3_ACCESS_KEY:-}"
S3_SECRET_KEY="${BACKUP_S3_SECRET_KEY:-}"

PG_IMAGE="${RESTORE_POSTGRES_IMAGE:-}"
PG_DATABASE="education_platform"
MIN_APPLICATION_TABLES=100
KNOWN_PROBLEMATIC_GRANT='GRANT pg_monitor TO postgres_exporter WITH INHERIT TRUE GRANTED BY platform;'

DRILL=false
KEEP=false
LATEST=false
OBJECT_KEY=""
WORK_DIR=""
CONTAINER_NAME=""
VOLUME_NAME=""

usage() {
    cat <<'EOF'
Usage:
  scripts/restore-s3.sh --drill --latest [--keep]
  scripts/restore-s3.sh --drill <object-key> [--keep]

Safely validates a PostgreSQL backup from S3 by restoring it into a disposable
pgvector/PostgreSQL container. This script has no production-target restore mode.

Options:
  --drill       Required safety flag. Restore only into disposable resources.
  --latest      Select the most recently uploaded *.sql.gz object.
  --keep        Keep the drill container, volume, and temporary files for debug.
  -h, --help    Show this help.

Object key:
  A bucket-relative key such as backups/backup_20260716_030000.sql.gz, or an
  s3:// URI in BACKUP_S3_BUCKET. Exactly one of --latest or <object-key> is
  required.

Required environment:
  BACKUP_S3_ACCESS_KEY
  BACKUP_S3_SECRET_KEY
  RESTORE_POSTGRES_IMAGE
                        Audited PostgreSQL 16 + pgvector image for this backup

Optional environment:
  BACKUP_S3_BUCKET      Default: education-platform-backups
  BACKUP_S3_ENDPOINT    Default: https://storage.yandexcloud.net
EOF
}

log() {
    printf '[%s UTC] %s\n' "$(date -u '+%Y-%m-%d %H:%M:%S')" "$*"
}

die() {
    printf 'ERROR: %s\n' "$*" >&2
    exit 1
}

cleanup() {
    local status=$?
    trap - EXIT

    if [[ "$KEEP" == true ]]; then
        log "Keeping drill resources: container=${CONTAINER_NAME:-not-created}, volume=${VOLUME_NAME:-not-created}, workspace=${WORK_DIR:-not-created}"
    else
        if [[ -n "$CONTAINER_NAME" ]]; then
            docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
        fi
        if [[ -n "$VOLUME_NAME" ]]; then
            docker volume rm "$VOLUME_NAME" >/dev/null 2>&1 || true
        fi
        if [[ -n "$WORK_DIR" ]]; then
            rm -rf "$WORK_DIR"
        fi
    fi

    exit "$status"
}

while (($# > 0)); do
    case "$1" in
        --drill)
            DRILL=true
            ;;
        --latest)
            [[ "$LATEST" == false ]] || die "--latest may be specified only once"
            LATEST=true
            ;;
        --keep)
            KEEP=true
            ;;
        -h | --help)
            usage
            exit 0
            ;;
        --)
            shift
            if (($# > 0)); then
                [[ -z "$OBJECT_KEY" && $# -eq 1 ]] || die "provide exactly one S3 object key"
                OBJECT_KEY="$1"
            fi
            break
            ;;
        -*)
            die "unknown option: $1"
            ;;
        *)
            [[ -z "$OBJECT_KEY" ]] || die "provide exactly one S3 object key"
            OBJECT_KEY="$1"
            ;;
    esac
    shift
done

[[ "$DRILL" == true ]] || die "refusing to run without the required --drill safety flag"
if [[ "$LATEST" == true && -n "$OBJECT_KEY" ]]; then
    die "choose either --latest or an explicit S3 object key, not both"
fi
if [[ "$LATEST" == false && -z "$OBJECT_KEY" ]]; then
    die "choose either --latest or an explicit S3 object key"
fi

[[ -n "$S3_ACCESS_KEY" && -n "$S3_SECRET_KEY" ]] ||
    die "BACKUP_S3_ACCESS_KEY and BACKUP_S3_SECRET_KEY must be set"
[[ -n "$PG_IMAGE" ]] || die "RESTORE_POSTGRES_IMAGE must be set"
[[ "$PG_IMAGE" != gitlab-sachkov.ru:* && "$PG_IMAGE" != gitlab-sachkov.ru/* ]] ||
    die "RESTORE_POSTGRES_IMAGE must not depend on the retired registry"

for command in aws awk docker gzip mktemp sort tail; do
    command -v "$command" >/dev/null 2>&1 || die "required command not found: $command"
done

export AWS_ACCESS_KEY_ID="$S3_ACCESS_KEY"
export AWS_SECRET_ACCESS_KEY="$S3_SECRET_KEY"
export AWS_DEFAULT_REGION="ru-central1"

WORK_DIR="$(mktemp -d "${TMPDIR:-/tmp}/education-platform-restore.XXXXXX")"
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

if [[ "$LATEST" == true ]]; then
    log "Finding latest PostgreSQL backup in s3://${S3_BUCKET}/"
    OBJECT_KEY="$({
        aws s3 ls "s3://${S3_BUCKET}/" --recursive --endpoint-url "$S3_ENDPOINT"
    } | awk '$4 ~ /\.sql\.gz$/ { print $1 "\t" $2 "\t" $4 }' |
        LC_ALL=C sort -k1,1 -k2,2 |
        tail -n 1 |
        awk '{ print $3 }')"
    [[ -n "$OBJECT_KEY" ]] || die "no *.sql.gz backups found in s3://${S3_BUCKET}/"
fi

if [[ "$OBJECT_KEY" == s3://* ]]; then
    S3_PREFIX="s3://${S3_BUCKET}/"
    [[ "$OBJECT_KEY" == "$S3_PREFIX"* ]] ||
        die "S3 object URI must belong to BACKUP_S3_BUCKET (${S3_BUCKET})"
    OBJECT_KEY="${OBJECT_KEY#"$S3_PREFIX"}"
fi

[[ "$OBJECT_KEY" == *.sql.gz ]] || die "backup object must end with .sql.gz"
[[ "$OBJECT_KEY" != *$'\n'* && "$OBJECT_KEY" != *$'\r'* ]] ||
    die "backup object key contains an invalid newline"

ARCHIVE_PATH="$WORK_DIR/$(basename "$OBJECT_KEY")"
REMOVED_COUNT_PATH="$WORK_DIR/removed-grant-count"

log "Downloading s3://${S3_BUCKET}/${OBJECT_KEY}"
aws s3 cp "s3://${S3_BUCKET}/${OBJECT_KEY}" "$ARCHIVE_PATH" \
    --endpoint-url "$S3_ENDPOINT" \
    --quiet

if ! gzip -t "$ARCHIVE_PATH"; then
    die "backup failed gzip integrity check: ${OBJECT_KEY}"
fi
log "Backup passed gzip integrity check"

RESOURCE_SUFFIX="$(date -u '+%Y%m%d%H%M%S')-$$"
CONTAINER_NAME="education-platform-restore-${RESOURCE_SUFFIX}"
VOLUME_NAME="education-platform-restore-${RESOURCE_SUFFIX}"

docker volume create "$VOLUME_NAME" >/dev/null
docker run -d \
    --name "$CONTAINER_NAME" \
    --network none \
    -e POSTGRES_HOST_AUTH_METHOD=trust \
    -v "$VOLUME_NAME:/var/lib/postgresql/data" \
    "$PG_IMAGE" >/dev/null

READY=false
for _ in {1..60}; do
    # The entrypoint's temporary initialization server accepts only Unix sockets.
    if docker exec "$CONTAINER_NAME" pg_isready -h 127.0.0.1 -U postgres -d postgres >/dev/null 2>&1; then
        READY=true
        break
    fi
    sleep 1
done
[[ "$READY" == true ]] || die "disposable PostgreSQL did not become ready within 60 seconds"

log "Restoring into disposable container ${CONTAINER_NAME}"
if ! gzip -dc "$ARCHIVE_PATH" |
    awk -v target="$KNOWN_PROBLEMATIC_GRANT" -v count_file="$REMOVED_COUNT_PATH" '
        BEGIN { removed = 0 }
        $0 == target { removed += 1; next }
        { print }
        END { print removed > count_file }
    ' |
    docker exec -i "$CONTAINER_NAME" psql -v ON_ERROR_STOP=1 -U postgres -d postgres; then
    die "PostgreSQL restore failed"
fi

REMOVED_COUNT="$(<"$REMOVED_COUNT_PATH")"
[[ "$REMOVED_COUNT" =~ ^[0-9]+$ ]] || die "failed to count filtered grant statements"
log "Filtered ${REMOVED_COUNT} known problematic pg_monitor grant statement(s)"

docker exec "$CONTAINER_NAME" \
    psql -v ON_ERROR_STOP=1 -U postgres -d postgres \
    -c 'GRANT pg_monitor TO postgres_exporter;' >/dev/null

psql_scalar() {
    local database="$1"
    local sql="$2"

    docker exec "$CONTAINER_NAME" \
        psql -v ON_ERROR_STOP=1 -At -U postgres -d "$database" -c "$sql"
}

DATABASE_EXISTS="$(psql_scalar postgres "SELECT 1 FROM pg_database WHERE datname = '${PG_DATABASE}';")"
[[ "$DATABASE_EXISTS" == "1" ]] || die "restored database ${PG_DATABASE} was not found"

# Full historical backups retain trainer data until a separately approved retirement.
APPLICATION_SCHEMAS="'access','assignment_review','auth','comments','education','files','material_processing','notifications','progress','search','tags','telegrambot','trainer'"
EXPECTED_SCHEMA_COUNT=13
SCHEMA_COUNT="$(psql_scalar "$PG_DATABASE" "SELECT count(*) FROM pg_namespace WHERE nspname IN (${APPLICATION_SCHEMAS});")"
[[ "$SCHEMA_COUNT" =~ ^[0-9]+$ && "$SCHEMA_COUNT" -eq "$EXPECTED_SCHEMA_COUNT" ]] ||
    die "application schema verification failed: expected ${EXPECTED_SCHEMA_COUNT}, found ${SCHEMA_COUNT}"

TABLE_COUNT="$(psql_scalar "$PG_DATABASE" "SELECT count(*) FROM pg_tables WHERE schemaname IN (${APPLICATION_SCHEMAS});")"
[[ "$TABLE_COUNT" =~ ^[0-9]+$ && "$TABLE_COUNT" -ge "$MIN_APPLICATION_TABLES" ]] ||
    die "application table verification failed: expected at least ${MIN_APPLICATION_TABLES}, found ${TABLE_COUNT}"

CONTROL_COUNTS="$(docker exec "$CONTAINER_NAME" \
    psql -v ON_ERROR_STOP=1 -At -F '|' -U postgres -d "$PG_DATABASE" -c \
    "SELECT 'auth.users', count(*) FROM auth.users
     UNION ALL SELECT 'education.courses', count(*) FROM education.courses
     UNION ALL SELECT 'access.plan_grants', count(*) FROM access.plan_grants;")"

CONTROL_TABLES_SEEN=0
while IFS='|' read -r table_name row_count; do
    [[ -n "$table_name" && "$row_count" =~ ^[0-9]+$ ]] ||
        die "invalid control-table verification result"
    [[ "$row_count" -gt 0 ]] || die "control table ${table_name} is empty"
    log "Control table ${table_name}: ${row_count} row(s)"
    ((CONTROL_TABLES_SEEN += 1))
done <<< "$CONTROL_COUNTS"
[[ "$CONTROL_TABLES_SEEN" -eq 3 ]] ||
    die "control-table verification failed: expected 3 results, found ${CONTROL_TABLES_SEEN}"

log "Restore drill passed: schemas=${SCHEMA_COUNT}, application_tables=${TABLE_COUNT}, control_tables=${CONTROL_TABLES_SEEN}"
