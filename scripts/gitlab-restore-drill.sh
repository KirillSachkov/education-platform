#!/usr/bin/env bash
set -euo pipefail

umask 077

BUCKET_URI="${GITLAB_BACKUP_BUCKET_URI:-s3://example-gitlab-backups/data}"
MAX_AGE_HOURS="${GITLAB_RESTORE_DRILL_MAX_AGE_HOURS:-36}"
LIVE_CONTAINER_NAMES="${GITLAB_LIVE_CONTAINER_NAMES:-gitlab,gitlab-sachkov,gitlab-ee}"
READY_TIMEOUT_SECONDS="${GITLAB_RESTORE_DRILL_READY_TIMEOUT_SECONDS:-900}"
S3CMD_BIN="${GITLAB_BACKUP_S3CMD_BIN:-s3cmd}"
DOCKER_BIN="${GITLAB_RESTORE_DRILL_DOCKER_BIN:-docker}"

mode="validate"
object_uri=""
target_container=""
confirmed_target=""
source_edition=""
validated_gitlab_version=""

log() {
  printf '[%s] %s\n' "$(date -u '+%Y-%m-%dT%H:%M:%SZ')" "$*"
}

fail() {
  log "ERROR: $*" >&2
  exit 1
}

usage() {
  cat <<'USAGE'
Usage:
  gitlab-restore-drill.sh [--validate-only] [--object s3://bucket/archive]
  gitlab-restore-drill.sh --execute --target-container NAME --confirm-target NAME \
    --source-edition ce|ee [--object URI]

The default validates the freshest offsite archive and never changes a GitLab instance.
Execution is allowed only for an explicitly labelled disposable container whose GitLab
version and CE/EE edition match the archive exactly.
USAGE
}

require_command() {
  command -v "$1" >/dev/null 2>&1 || fail "required command not found: $1"
}

file_size() {
  wc -c < "$1" | tr -d '[:space:]'
}

remote_size() {
  local remote_object="$1"

  "$S3CMD_BIN" ls "$remote_object" 2>/dev/null \
    | awk -v remote_object="$remote_object" '$4 == remote_object { print $3; exit }'
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
  validated_gitlab_version="$metadata_version"
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --validate-only)
      mode="validate"
      shift
      ;;
    --execute)
      mode="execute"
      shift
      ;;
    --object)
      [[ $# -ge 2 ]] || fail "--object requires a value"
      object_uri="$2"
      shift 2
      ;;
    --target-container)
      [[ $# -ge 2 ]] || fail "--target-container requires a value"
      target_container="$2"
      shift 2
      ;;
    --confirm-target)
      [[ $# -ge 2 ]] || fail "--confirm-target requires a value"
      confirmed_target="$2"
      shift 2
      ;;
    --source-edition)
      [[ $# -ge 2 ]] || fail "--source-edition requires a value"
      source_edition="$2"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      fail "unknown argument: $1"
      ;;
  esac
done

for command_name in awk tar gzip wc tr sed grep sort date "$S3CMD_BIN"; do
  require_command "$command_name"
done

[[ "$MAX_AGE_HOURS" =~ ^[0-9]+$ && "$MAX_AGE_HOURS" -gt 0 ]] \
  || fail "GITLAB_RESTORE_DRILL_MAX_AGE_HOURS must be a positive integer"
[[ "$READY_TIMEOUT_SECONDS" =~ ^[0-9]+$ && "$READY_TIMEOUT_SECONDS" -gt 0 ]] \
  || fail "GITLAB_RESTORE_DRILL_READY_TIMEOUT_SECONDS must be a positive integer"

BUCKET_URI="${BUCKET_URI%/}"
if [[ -z "$object_uri" ]]; then
  object_uri="$("$S3CMD_BIN" ls "${BUCKET_URI}/" 2>/dev/null \
    | awk '$4 ~ /_gitlab_backup\.tar$/ { print $4 }' \
    | sort \
    | tail -n 1)"
fi

[[ -n "$object_uri" ]] || fail "no GitLab data backup found in ${BUCKET_URI}"
case "$object_uri" in
  "${BUCKET_URI}/"*_gitlab_backup.tar) ;;
  *) fail "object must be a GitLab backup inside ${BUCKET_URI}" ;;
esac

filename="$(basename "$object_uri")"
backup_epoch="${filename%%_*}"
[[ "$backup_epoch" =~ ^[0-9]+$ ]] || fail "backup filename has no Unix timestamp"

now_epoch="$(date +%s)"
if (( backup_epoch > now_epoch + 300 )); then
  fail "backup timestamp is unexpectedly in the future"
fi
age_seconds=$((now_epoch - backup_epoch))
max_age_seconds=$((MAX_AGE_HOURS * 3600))
if (( age_seconds > max_age_seconds )); then
  fail "freshest offsite backup is stale: age=${age_seconds}s limit=${max_age_seconds}s"
fi

"$S3CMD_BIN" info "$object_uri" >/dev/null 2>&1 \
  || fail "remote object HEAD failed for ${object_uri}"
expected_size="$(remote_size "$object_uri")"
[[ "$expected_size" =~ ^[0-9]+$ && "$expected_size" -gt 0 ]] \
  || fail "remote object has no valid size"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT
archive="${work_dir}/${filename}"

log "Downloading offsite archive for validation: ${object_uri}"
"$S3CMD_BIN" --quiet get --force "$object_uri" "$archive"
actual_size="$(file_size "$archive")"
[[ "$actual_size" == "$expected_size" ]] \
  || fail "downloaded size differs from remote object size"

validate_archive "$archive" "$work_dir/archive.list"
log "Offsite archive validated: ${filename} (${actual_size} bytes, age=${age_seconds}s)"

if [[ "$mode" == "validate" ]]; then
  log "Validation-only drill completed; no GitLab instance was changed"
  exit 0
fi

require_command "$DOCKER_BIN"
[[ "$target_container" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] \
  || fail "--target-container must be a valid explicit container name"
[[ "$confirmed_target" == "$target_container" ]] \
  || fail "--confirm-target must exactly match --target-container"
[[ "$source_edition" =~ ^(ce|ee)$ ]] \
  || fail "--source-edition must explicitly be ce or ee"

IFS=',' read -r -a live_names <<< "$LIVE_CONTAINER_NAMES"
for live_name in "${live_names[@]}"; do
  if [[ "$target_container" == "$live_name" ]]; then
    fail "refusing known live container name: ${target_container}"
  fi
done

restore_label="$("$DOCKER_BIN" inspect \
  --format '{{ index .Config.Labels "com.sachkov.gitlab-restore-drill" }}' \
  "$target_container" 2>/dev/null || true)"
[[ "$restore_label" == "true" ]] \
  || fail "target must have label com.sachkov.gitlab-restore-drill=true"

running="$("$DOCKER_BIN" inspect --format '{{.State.Running}}' "$target_container")"
[[ "$running" == "true" ]] || fail "target container is not running"

target_id="$("$DOCKER_BIN" inspect --format '{{.Id}}' "$target_container")"
[[ -n "$target_id" ]] || fail "could not determine target container id"
mount_table="$("$DOCKER_BIN" inspect \
  --format '{{range .Mounts}}{{printf "%s|%s|%s\n" .Type .Name .Destination}}{{end}}' \
  "$target_container")"

for required_destination in /etc/gitlab /var/log/gitlab /var/opt/gitlab; do
  mount_line="$(printf '%s\n' "$mount_table" \
    | awk -F '|' -v destination="$required_destination" \
      '$3 == destination { count += 1; line = $0 } END { if (count == 1) print line }')"
  [[ -n "$mount_line" ]] \
    || fail "target ${required_destination} must use exactly one dedicated labelled volume"

  IFS='|' read -r mount_type volume_name _ <<< "$mount_line"
  [[ "$mount_type" == "volume" && -n "$volume_name" ]] \
    || fail "target ${required_destination} must use a dedicated labelled volume, not a bind mount"

  volume_label="$("$DOCKER_BIN" volume inspect \
    --format '{{ index .Labels "com.sachkov.gitlab-restore-drill" }}' \
    "$volume_name" 2>/dev/null || true)"
  [[ "$volume_label" == "true" ]] \
    || fail "volume ${volume_name} must have label com.sachkov.gitlab-restore-drill=true"

  target_uses_volume=false
  while IFS= read -r container_id; do
    [[ -n "$container_id" ]] || continue
    if [[ "$container_id" == "$target_id" ]]; then
      target_uses_volume=true
    else
      fail "volume ${volume_name} is shared with another container"
    fi
  done < <("$DOCKER_BIN" ps -aq --no-trunc --filter "volume=${volume_name}")
  [[ "$target_uses_volume" == true ]] \
    || fail "target container does not own volume ${volume_name}"
done

backup_id="${filename%_gitlab_backup.tar}"
[[ -n "$validated_gitlab_version" ]] || fail "validated archive version is unavailable"

target_image="$("$DOCKER_BIN" inspect --format '{{.Config.Image}}' "$target_container")"
case "$target_image" in
  *gitlab-ee*) target_edition="ee" ;;
  *gitlab-ce*) target_edition="ce" ;;
  *) fail "target image does not identify GitLab CE or EE" ;;
esac
[[ "$target_edition" == "$source_edition" ]] \
  || fail "target GitLab edition differs from backup"

installed_version="$("$DOCKER_BIN" exec "$target_container" \
  gitlab-rails runner 'puts Gitlab::VERSION' \
  | awk 'NF { value=$0 } END { gsub(/^[[:space:]]+|[[:space:]]+$/, "", value); print value }')"
[[ "$installed_version" == "$validated_gitlab_version" ]] \
  || fail "target GitLab version ${installed_version} differs from backup ${validated_gitlab_version}"

"$DOCKER_BIN" exec "$target_container" test -f /etc/gitlab/gitlab-secrets.json \
  || fail "target has no gitlab-secrets.json; provision matching secrets before restore"

target_archive="/var/opt/gitlab/backups/${filename}"
log "Copying validated archive into disposable target ${target_container}"
"$DOCKER_BIN" exec -u 0 "$target_container" mkdir -p /var/opt/gitlab/backups
"$DOCKER_BIN" cp "$archive" "${target_container}:${target_archive}"
"$DOCKER_BIN" exec -u 0 "$target_container" chown git:git "$target_archive"

log "Stopping Puma and Sidekiq in disposable target"
"$DOCKER_BIN" exec "$target_container" gitlab-ctl stop puma
"$DOCKER_BIN" exec "$target_container" gitlab-ctl stop sidekiq

log "Restoring backup into disposable target"
"$DOCKER_BIN" exec -e GITLAB_ASSUME_YES=1 "$target_container" \
  gitlab-backup restore "BACKUP=${backup_id}" SKIP=registry

"$DOCKER_BIN" restart "$target_container" >/dev/null
deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
until "$DOCKER_BIN" exec "$target_container" gitlab-ctl status >/dev/null 2>&1; do
  (( SECONDS < deadline )) || fail "target did not become ready before timeout"
  sleep 10
done

log "Running post-restore GitLab and secrets checks"
"$DOCKER_BIN" exec "$target_container" gitlab-rake gitlab:check SANITIZE=true
"$DOCKER_BIN" exec "$target_container" gitlab-rake gitlab:doctor:secrets
"$DOCKER_BIN" exec -u 0 "$target_container" rm -f -- "$target_archive"

log "Disposable same-version GitLab restore drill completed successfully"
