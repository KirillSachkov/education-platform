#!/usr/bin/env bash
set -euo pipefail

umask 077

BACKUP_DIR="${GITLAB_BACKUP_DIR:-/var/opt/gitlab/backups}"
BUCKET_URI="${GITLAB_BACKUP_BUCKET_URI:-s3://example-gitlab-backups/data}"
LOCK_FILE="${GITLAB_BACKUP_LOCK_FILE:-/run/lock/gitlab-data-backup.lock}"
MIN_FREE_BYTES="${GITLAB_BACKUP_MIN_FREE_BYTES:-6442450944}"
LOCAL_RETENTION_DAYS="${GITLAB_BACKUP_LOCAL_RETENTION_DAYS:-2}"
GITLAB_BACKUP_BIN="${GITLAB_BACKUP_BIN:-gitlab-backup}"
GITLAB_RAILS_BIN="${GITLAB_BACKUP_RAILS_BIN:-gitlab-rails}"
S3CMD_BIN="${GITLAB_BACKUP_S3CMD_BIN:-s3cmd}"

log() {
  printf '[%s] %s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$*"
}

fail() {
  log "ERROR: $*" >&2
  exit 1
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

file_size() {
  wc -c < "$1" | tr -d '[:space:]'
}

remote_size() {
  local object_uri="$1"

  "$S3CMD_BIN" ls "$object_uri" 2>/dev/null \
    | awk -v object_uri="$object_uri" '$4 == object_uri { print $3; exit }'
}

remote_object_verified() {
  local object_uri="$1"
  local expected_size="$2"
  local actual_size

  "$S3CMD_BIN" info "$object_uri" >/dev/null 2>&1 || return 1
  actual_size="$(remote_size "$object_uri")"
  [[ "$actual_size" =~ ^[0-9]+$ && "$actual_size" == "$expected_size" ]]
}

validate_archive() {
  local archive="$1"
  local list_file="$2"
  local metadata_entry db_entry metadata skipped_value metadata_version backup_id

  tar -tf "$archive" > "$list_file" || fail "tar validation failed for $(basename "$archive")"

  metadata_entry="$(awk '$0 == "backup_information.yml" || $0 == "./backup_information.yml" { print; exit }' "$list_file")"
  db_entry="$(awk '$0 == "db/database.sql.gz" || $0 == "./db/database.sql.gz" { print; exit }' "$list_file")"

  [[ -n "$metadata_entry" ]] || fail "archive is missing backup_information.yml"
  [[ -n "$db_entry" ]] || fail "archive is missing db/database.sql.gz"
  grep -Eq '^(\./)?repositories/' "$list_file" || fail "archive is missing repository data"
  if grep -Eq '^(\./)?registry(\.tar\.gz|/)' "$list_file"; then
    fail "archive unexpectedly contains registry data"
  fi

  tar -xOf "$archive" "$db_entry" | gzip -t \
    || fail "database payload is not a valid gzip stream"

  metadata="$(tar -xOf "$archive" "$metadata_entry")"
  metadata_version="$(printf '%s\n' "$metadata" \
    | sed -n 's/^:gitlab_version:[[:space:]]*//p' \
    | head -n 1 \
    | tr -d " '\"")"
  skipped_value="$(printf '%s\n' "$metadata" \
    | sed -n 's/^:skipped:[[:space:]]*//p' \
    | head -n 1 \
    | tr -d " '\"[]")"

  [[ -n "$metadata_version" ]] || fail "archive metadata has no GitLab version"
  case ",${skipped_value}," in
    *,registry,*) ;;
    *) fail "archive metadata does not record registry as skipped" ;;
  esac

  backup_id="$(basename "$archive" _gitlab_backup.tar)"
  [[ "$backup_id" == *"_${metadata_version}" ]] \
    || fail "archive filename and metadata GitLab versions differ"
}

for command_name in flock df awk tar gzip find sort wc tr sed grep \
  "$GITLAB_BACKUP_BIN" "$GITLAB_RAILS_BIN" "$S3CMD_BIN"; do
  require_command "$command_name"
done

[[ "$MIN_FREE_BYTES" =~ ^[0-9]+$ ]] || fail "GITLAB_BACKUP_MIN_FREE_BYTES must be an integer"
[[ "$LOCAL_RETENTION_DAYS" =~ ^[0-9]+$ ]] || fail "GITLAB_BACKUP_LOCAL_RETENTION_DAYS must be an integer"

mkdir -p "$BACKUP_DIR" "$(dirname "$LOCK_FILE")"

exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  log "Another GitLab backup is already running; this invocation is skipped"
  exit 0
fi

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT
touch "$work_dir/backup-started"

backup_keep_time="$("$GITLAB_RAILS_BIN" runner \
  'puts Gitlab.config.backup.keep_time.to_i' \
  | awk 'NF { value=$0 } END { gsub(/^[[:space:]]+|[[:space:]]+$/, "", value); print value }')"
[[ "$backup_keep_time" =~ ^[0-9]+$ ]] \
  || fail "could not determine GitLab backup_keep_time"
if (( backup_keep_time != 0 )); then
  fail "GitLab backup_keep_time must be 0; wrapper-managed verified deletion is required"
fi

available_kib="$(df -Pk "$BACKUP_DIR" | awk 'NR == 2 { print $4; exit }')"
[[ "$available_kib" =~ ^[0-9]+$ ]] || fail "could not determine free space for $BACKUP_DIR"
available_bytes=$((available_kib * 1024))
if (( available_bytes < MIN_FREE_BYTES )); then
  fail "insufficient free space: available=${available_bytes}B required=${MIN_FREE_BYTES}B"
fi

log "Starting GitLab data backup with registry and GitLab remote upload skipped"
"$GITLAB_BACKUP_BIN" create SKIP=registry,remote

archive="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name '*_gitlab_backup.tar' \
  -newer "$work_dir/backup-started" -print | sort | tail -n 1)"
[[ -n "$archive" && -f "$archive" ]] || fail "gitlab-backup did not create a new archive"

validate_archive "$archive" "$work_dir/archive.list"
archive_size="$(file_size "$archive")"
[[ "$archive_size" =~ ^[0-9]+$ && "$archive_size" -gt 0 ]] \
  || fail "archive has an invalid size"
log "Archive validated: $(basename "$archive") (${archive_size} bytes)"

BUCKET_URI="${BUCKET_URI%/}"
object_uri="${BUCKET_URI}/$(basename "$archive")"
log "Uploading archive to ${object_uri}"
"$S3CMD_BIN" --quiet put "$archive" "$object_uri"

remote_object_verified "$object_uri" "$archive_size" \
  || fail "remote object verification failed for ${object_uri}"
log "Remote object verified by HEAD/list and size"

deleted_count=0
while IFS= read -r -d '' local_archive; do
  [[ "$local_archive" == "$archive" ]] && continue

  local_size="$(file_size "$local_archive")"
  local_object_uri="${BUCKET_URI}/$(basename "$local_archive")"
  if (validate_archive "$local_archive" "$work_dir/old-archive.list") \
    && remote_object_verified "$local_object_uri" "$local_size"; then
    rm -f -- "$local_archive"
    deleted_count=$((deleted_count + 1))
    log "Deleted verified old local archive: $(basename "$local_archive")"
  else
    log "Keeping unverified old local archive: $(basename "$local_archive")"
  fi
done < <(find "$BACKUP_DIR" -maxdepth 1 -type f -name '*_gitlab_backup.tar' \
  -mtime +"$LOCAL_RETENTION_DAYS" -print0)

log "GitLab backup completed: remote=${object_uri} local_old_deleted=${deleted_count}"
