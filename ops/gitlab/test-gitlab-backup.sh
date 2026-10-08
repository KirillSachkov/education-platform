#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BACKUP_SCRIPT="${ROOT_DIR}/scripts/gitlab-backup.sh"
RESTORE_SCRIPT="${ROOT_DIR}/scripts/gitlab-restore-drill.sh"

test_dir="$(mktemp -d)"
trap 'rm -rf "$test_dir"' EXIT

fake_bin="${test_dir}/bin"
backup_dir="${test_dir}/backups"
remote_dir="${test_dir}/remote"
mkdir -p "$fake_bin" "$backup_dir" "$remote_dir"

assert_contains() {
  local file="$1"
  local expected="$2"

  grep -Fq "$expected" "$file" || {
    printf 'Expected %s to contain: %s\n' "$file" "$expected" >&2
    exit 1
  }
}

create_fixture_archive() {
  local archive="$1"
  local version="${2:-18.2.1-ee}"
  local include_repositories="${3:-true}"
  local staging

  staging="$(mktemp -d "${test_dir}/staging.XXXXXX")"
  mkdir -p "${staging}/db"
  printf 'SELECT 1;\n' | gzip > "${staging}/db/database.sql.gz"
  if [[ "$include_repositories" == "true" ]]; then
    mkdir -p "${staging}/repositories/default"
    printf 'fixture bundle\n' > "${staging}/repositories/default/project.bundle"
  fi
  {
    printf ':backup_created_at: 2026-07-16 00:00:00 UTC\n'
    printf ':gitlab_version: %s\n' "$version"
    printf ':installation_type: omnibus-gitlab\n'
    printf ':skipped: registry,remote\n'
  } > "${staging}/backup_information.yml"
  tar -cf "$archive" -C "$staging" .
  rm -rf "$staging"
}

cat > "${fake_bin}/df" <<'FAKE_DF'
#!/usr/bin/env bash
set -euo pipefail
printf 'Filesystem 1024-blocks Used Available Capacity Mounted on\n'
printf '/dev/test 10000000 1 %s 1%% /test\n' "${TEST_AVAILABLE_KIB:-9000000}"
FAKE_DF

cat > "${fake_bin}/gitlab-backup" <<'FAKE_GITLAB_BACKUP'
#!/usr/bin/env bash
set -euo pipefail
printf 'gitlab-backup %s\n' "$*" >> "${TEST_COMMAND_LOG}"
sleep 1
staging="$(mktemp -d "${TEST_ROOT}/generated.XXXXXX")"
mkdir -p "${staging}/db" "${staging}/repositories/default"
printf 'SELECT 1;\n' | gzip > "${staging}/db/database.sql.gz"
printf 'generated bundle\n' > "${staging}/repositories/default/project.bundle"
{
  printf ':backup_created_at: 2026-07-16 00:00:00 UTC\n'
  printf ':gitlab_version: 18.2.1-ee\n'
  printf ':installation_type: omnibus-gitlab\n'
  printf ':skipped: registry,remote\n'
} > "${staging}/backup_information.yml"
archive="${TEST_BACKUP_DIR}/$(date +%s)_$(date +%Y_%m_%d)_18.2.1-ee_gitlab_backup.tar"
tar -cf "$archive" -C "$staging" .
rm -rf "$staging"
FAKE_GITLAB_BACKUP

cat > "${fake_bin}/gitlab-rails" <<'FAKE_GITLAB_RAILS'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "${TEST_BACKUP_KEEP_TIME:-0}"
FAKE_GITLAB_RAILS

cat > "${fake_bin}/s3cmd" <<'FAKE_S3CMD'
#!/usr/bin/env bash
set -euo pipefail
while [[ $# -gt 0 && "$1" == --* ]]; do
  shift
done
command_name="${1:-}"
shift || true

object_path() {
  printf '%s/%s\n' "$TEST_REMOTE_DIR" "$(basename "$1")"
}

case "$command_name" in
  put)
    source_file="$1"
    object_uri="$2"
    cp "$source_file" "$(object_path "$object_uri")"
    printf 's3cmd put %s\n' "$object_uri" >> "$TEST_COMMAND_LOG"
    ;;
  info)
    object_uri="$1"
    test -f "$(object_path "$object_uri")"
    ;;
  ls)
    object_uri="$1"
    object_file="$(object_path "$object_uri")"
    if [[ "$object_uri" == *_gitlab_backup.tar && -f "$object_file" ]]; then
      size="$(wc -c < "$object_file" | tr -d '[:space:]')"
      printf '2026-07-16 00:00 %s %s\n' "$size" "$object_uri"
    elif [[ "$object_uri" == */ ]]; then
      for file in "$TEST_REMOTE_DIR"/*_gitlab_backup.tar; do
        [[ -e "$file" ]] || continue
        size="$(wc -c < "$file" | tr -d '[:space:]')"
        printf '2026-07-16 00:00 %s %s%s\n' "$size" "$object_uri" "$(basename "$file")"
      done
    fi
    ;;
  get)
    if [[ "${1:-}" == "--force" ]]; then
      shift
    fi
    object_uri="$1"
    destination="$2"
    cp "$(object_path "$object_uri")" "$destination"
    ;;
  *)
    printf 'Unexpected fake s3cmd command: %s\n' "$command_name" >&2
    exit 2
    ;;
esac
FAKE_S3CMD

cat > "${fake_bin}/docker" <<'FAKE_DOCKER'
#!/usr/bin/env bash
set -euo pipefail
[[ "${TEST_DOCKER_MODE:-fail}" == "success" ]] || exit 99
printf 'docker %s\n' "$*" >> "$TEST_COMMAND_LOG"

case "${1:-}" in
  inspect)
    case "${3:-}" in
      *gitlab-restore-drill*) printf 'true\n' ;;
      *State.Running*) printf 'true\n' ;;
      *'.Id'*) printf '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n' ;;
      *Mounts*)
        if [[ "${TEST_DOCKER_MOUNTS:-safe}" == "unsafe-bind" ]]; then
          printf 'bind||/etc/gitlab\nbind||/var/log/gitlab\nbind||/var/opt/gitlab\n'
        else
          printf 'volume|restore-config|/etc/gitlab\n'
          printf 'volume|restore-logs|/var/log/gitlab\n'
          printf 'volume|restore-data|/var/opt/gitlab\n'
        fi
        ;;
      *Config.Image*) printf 'gitlab/gitlab-%s:%s\n' \
        "${TEST_DOCKER_EDITION:-ee}" "${TEST_DOCKER_IMAGE_VERSION:-18.2.1-ee.0}" ;;
      *) exit 2 ;;
    esac
    ;;
  volume)
    [[ "${2:-}" == "inspect" ]] || exit 2
    printf '%s\n' "${TEST_DOCKER_VOLUME_LABEL:-true}"
    ;;
  ps)
    if [[ " $* " == *' --no-trunc '* ]]; then
      printf '0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n'
      if [[ "${TEST_DOCKER_SHARED_VOLUME:-false}" == "true" ]]; then
        printf 'fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210\n'
      fi
    else
      printf '0123456789ab\n'
    fi
    ;;
  exec)
    if [[ "$*" == *"gitlab-rails runner"* ]]; then
      printf '%s\n' "${TEST_DOCKER_VERSION:-18.2.1-ee}"
    fi
    ;;
  cp|restart)
    ;;
  *)
    exit 2
    ;;
esac
FAKE_DOCKER

cat > "${fake_bin}/flock" <<'FAKE_FLOCK'
#!/usr/bin/env bash
exit 0
FAKE_FLOCK

chmod +x "${fake_bin}/df" "${fake_bin}/gitlab-backup" "${fake_bin}/gitlab-rails" \
  "${fake_bin}/s3cmd" "${fake_bin}/docker" "${fake_bin}/flock"

export PATH="${fake_bin}:${PATH}"
export TEST_ROOT="$test_dir"
export TEST_BACKUP_DIR="$backup_dir"
export TEST_REMOTE_DIR="$remote_dir"
export TEST_COMMAND_LOG="${test_dir}/commands.log"
unset GITLAB_BACKUP_BUCKET_URI
export GITLAB_RESTORE_DRILL_DOCKER_BIN="${fake_bin}/docker"
for command_name in df gitlab-backup gitlab-rails s3cmd docker flock; do
  [[ "$(command -v "$command_name")" == "${fake_bin}/${command_name}" ]] || {
    printf 'Test must use stubbed command: %s\n' "$command_name" >&2
    exit 1
  }
done

old_verified="1609459200_2021_01_01_18.2.1-ee_gitlab_backup.tar"
old_unverified="1609545600_2021_01_02_18.2.1-ee_gitlab_backup.tar"
create_fixture_archive "${backup_dir}/${old_verified}"
create_fixture_archive "${backup_dir}/${old_unverified}"
cp "${backup_dir}/${old_verified}" "${remote_dir}/${old_verified}"
touch -t 202101010000 "${backup_dir}/${old_verified}" "${backup_dir}/${old_unverified}"

backup_log="${test_dir}/backup.log"
GITLAB_BACKUP_DIR="$backup_dir" \
GITLAB_BACKUP_LOCK_FILE="${test_dir}/backup.lock" \
GITLAB_BACKUP_MIN_FREE_BYTES=1024 \
GITLAB_BACKUP_LOCAL_RETENTION_DAYS=0 \
GITLAB_BACKUP_BIN=gitlab-backup \
GITLAB_BACKUP_RAILS_BIN=gitlab-rails \
GITLAB_BACKUP_S3CMD_BIN=s3cmd \
"$BACKUP_SCRIPT" > "$backup_log" 2>&1 || {
  printf 'Backup wrapper failed unexpectedly:\n' >&2
  sed -n '1,200p' "$backup_log" >&2
  exit 1
}

assert_contains "$TEST_COMMAND_LOG" 'gitlab-backup create SKIP=registry,remote'
assert_contains "$TEST_COMMAND_LOG" 's3cmd put s3://example-gitlab-backups/data/'
assert_contains "$backup_log" 'Remote object verified by HEAD/list and size'
test ! -e "${backup_dir}/${old_verified}"
test -e "${backup_dir}/${old_unverified}"
new_remote_count="$(find "$remote_dir" -type f -name '*_gitlab_backup.tar' | wc -l | tr -d '[:space:]')"
[[ "$new_remote_count" == "2" ]]

restore_log="${test_dir}/restore.log"
GITLAB_BACKUP_S3CMD_BIN=s3cmd \
GITLAB_RESTORE_DRILL_MAX_AGE_HOURS=48 \
"$RESTORE_SCRIPT" --validate-only > "$restore_log" 2>&1 || {
  printf 'Restore validator failed unexpectedly:\n' >&2
  sed -n '1,200p' "$restore_log" >&2
  exit 1
}
assert_contains "$restore_log" 'Offsite archive validated'
assert_contains "$restore_log" 'no GitLab instance was changed'
assert_contains "$restore_log" 'Downloading offsite archive for validation: s3://example-gitlab-backups/data/'

override_uri='s3://example-gitlab-backups/custom-data'
GITLAB_BACKUP_DIR="$backup_dir" \
GITLAB_BACKUP_LOCK_FILE="${test_dir}/backup.lock" \
GITLAB_BACKUP_MIN_FREE_BYTES=1024 \
GITLAB_BACKUP_LOCAL_RETENTION_DAYS=0 \
GITLAB_BACKUP_BUCKET_URI="$override_uri" \
GITLAB_BACKUP_BIN=gitlab-backup \
GITLAB_BACKUP_RAILS_BIN=gitlab-rails \
GITLAB_BACKUP_S3CMD_BIN=s3cmd \
"$BACKUP_SCRIPT" > "${test_dir}/override-backup.log" 2>&1
assert_contains "$TEST_COMMAND_LOG" "s3cmd put ${override_uri}/"
GITLAB_BACKUP_BUCKET_URI="$override_uri" \
GITLAB_BACKUP_S3CMD_BIN=s3cmd \
"$RESTORE_SCRIPT" --validate-only > "${test_dir}/override-restore.log" 2>&1
assert_contains "${test_dir}/override-restore.log" "Downloading offsite archive for validation: ${override_uri}/"
assert_contains "${test_dir}/override-restore.log" 'no GitLab instance was changed'
override_object="$(find "$remote_dir" -type f -name '*_gitlab_backup.tar' -exec basename {} \; | sort | tail -n 1)"
if GITLAB_BACKUP_BUCKET_URI="$override_uri" \
  GITLAB_BACKUP_S3CMD_BIN=s3cmd \
  "$RESTORE_SCRIPT" --validate-only \
  --object "s3://example-gitlab-backups/data/${override_object}" \
  > "${test_dir}/outside-bucket.log" 2>&1; then
  printf 'Expected object outside the overridden bucket to fail\n' >&2
  exit 1
fi
assert_contains "${test_dir}/outside-bucket.log" "object must be a GitLab backup inside ${override_uri}"

low_disk_dir="${test_dir}/low-disk"
mkdir -p "$low_disk_dir"
if TEST_AVAILABLE_KIB=1 \
  GITLAB_BACKUP_DIR="$low_disk_dir" \
  GITLAB_BACKUP_LOCK_FILE="${test_dir}/low-disk.lock" \
  GITLAB_BACKUP_MIN_FREE_BYTES=2048 \
  GITLAB_BACKUP_BIN=gitlab-backup \
  GITLAB_BACKUP_RAILS_BIN=gitlab-rails \
  GITLAB_BACKUP_S3CMD_BIN=s3cmd \
  "$BACKUP_SCRIPT" > "${test_dir}/low-disk.log" 2>&1; then
  printf 'Expected low-space backup to fail\n' >&2
  exit 1
fi
assert_contains "${test_dir}/low-disk.log" 'insufficient free space'

retention_guard_dir="${test_dir}/retention-guard"
mkdir -p "$retention_guard_dir"
if TEST_BACKUP_KEEP_TIME=86400 \
  GITLAB_BACKUP_DIR="$retention_guard_dir" \
  GITLAB_BACKUP_LOCK_FILE="${test_dir}/retention-guard.lock" \
  GITLAB_BACKUP_MIN_FREE_BYTES=1024 \
  GITLAB_BACKUP_BIN=gitlab-backup \
  GITLAB_BACKUP_RAILS_BIN=gitlab-rails \
  GITLAB_BACKUP_S3CMD_BIN=s3cmd \
  "$BACKUP_SCRIPT" > "${test_dir}/retention-guard.log" 2>&1; then
  printf 'Expected non-zero GitLab backup_keep_time to fail closed\n' >&2
  exit 1
fi
assert_contains "${test_dir}/retention-guard.log" 'GitLab backup_keep_time must be 0'

bad_epoch=$(( $(date +%s) - 10 ))
bad_name="${bad_epoch}_$(date +%Y_%m_%d)_18.2.1-ee_gitlab_backup.tar"
create_fixture_archive "${remote_dir}/${bad_name}" 18.2.1-ee false
if GITLAB_BACKUP_BUCKET_URI=s3://example-gitlab-backups/data \
  GITLAB_BACKUP_S3CMD_BIN=s3cmd \
  "$RESTORE_SCRIPT" --validate-only \
  --object "s3://example-gitlab-backups/data/${bad_name}" \
  > "${test_dir}/bad-archive.log" 2>&1; then
  printf 'Expected structurally incomplete archive to fail\n' >&2
  exit 1
fi
assert_contains "${test_dir}/bad-archive.log" 'archive is missing repository data'
rm -f "${remote_dir}/${bad_name}"

latest_name="$(find "$remote_dir" -type f -name '*_gitlab_backup.tar' -exec basename {} \; | sort | tail -n 1)"
if GITLAB_BACKUP_BUCKET_URI=s3://example-gitlab-backups/data \
  GITLAB_BACKUP_S3CMD_BIN=s3cmd \
  GITLAB_RESTORE_DRILL_DOCKER_BIN=docker \
  "$RESTORE_SCRIPT" --execute --target-container gitlab --confirm-target gitlab \
  --source-edition ee \
  --object "s3://example-gitlab-backups/data/${latest_name}" \
  > "${test_dir}/live-target.log" 2>&1; then
  printf 'Expected known live target to be rejected\n' >&2
  exit 1
fi
assert_contains "${test_dir}/live-target.log" 'refusing known live container name: gitlab'

if TEST_DOCKER_MODE=success \
  TEST_DOCKER_MOUNTS=unsafe-bind \
  GITLAB_BACKUP_BUCKET_URI=s3://example-gitlab-backups/data \
  GITLAB_BACKUP_S3CMD_BIN=s3cmd \
  GITLAB_RESTORE_DRILL_DOCKER_BIN=docker \
  GITLAB_LIVE_CONTAINER_NAMES=gitlab \
  "$RESTORE_SCRIPT" --execute \
  --target-container gitlab-restore-drill \
  --confirm-target gitlab-restore-drill \
  --source-edition ee \
  --object "s3://example-gitlab-backups/data/${latest_name}" \
  > "${test_dir}/unsafe-mount.log" 2>&1; then
  printf 'Expected restore target with live bind mounts to be rejected\n' >&2
  exit 1
fi
assert_contains "${test_dir}/unsafe-mount.log" 'dedicated labelled volume'

if TEST_DOCKER_MODE=success \
  TEST_DOCKER_SHARED_VOLUME=true \
  GITLAB_BACKUP_BUCKET_URI=s3://example-gitlab-backups/data \
  GITLAB_BACKUP_S3CMD_BIN=s3cmd \
  GITLAB_RESTORE_DRILL_DOCKER_BIN=docker \
  GITLAB_LIVE_CONTAINER_NAMES=gitlab \
  "$RESTORE_SCRIPT" --execute \
  --target-container gitlab-restore-drill \
  --confirm-target gitlab-restore-drill \
  --source-edition ee \
  --object "s3://example-gitlab-backups/data/${latest_name}" \
  > "${test_dir}/shared-volume.log" 2>&1; then
  printf 'Expected restore target with a shared data volume to be rejected\n' >&2
  exit 1
fi
assert_contains "${test_dir}/shared-volume.log" 'shared with another container'

TEST_DOCKER_MODE=success \
GITLAB_BACKUP_BUCKET_URI=s3://example-gitlab-backups/data \
GITLAB_BACKUP_S3CMD_BIN=s3cmd \
GITLAB_RESTORE_DRILL_DOCKER_BIN=docker \
GITLAB_LIVE_CONTAINER_NAMES=gitlab \
"$RESTORE_SCRIPT" --execute \
--target-container gitlab-restore-drill \
--confirm-target gitlab-restore-drill \
--source-edition ee \
--object "s3://example-gitlab-backups/data/${latest_name}" \
> "${test_dir}/disposable-target.log" 2>&1 || {
  printf 'Expected labelled same-version disposable restore path to run:\n' >&2
  sed -n '1,200p' "${test_dir}/disposable-target.log" >&2
  exit 1
}
assert_contains "$TEST_COMMAND_LOG" "gitlab-backup restore BACKUP=${latest_name%_gitlab_backup.tar} SKIP=registry"
assert_contains "$TEST_COMMAND_LOG" 'ps -aq --no-trunc --filter volume=restore-data'
assert_contains "${test_dir}/disposable-target.log" 'Disposable same-version GitLab restore drill completed successfully'

ce_epoch=$(( $(date +%s) - 20 ))
ce_name="${ce_epoch}_$(date +%Y_%m_%d)_18.10.1_gitlab_backup.tar"
create_fixture_archive "${remote_dir}/${ce_name}" 18.10.1 true
TEST_DOCKER_MODE=success \
TEST_DOCKER_EDITION=ce \
TEST_DOCKER_IMAGE_VERSION=18.10.1-ce.0 \
TEST_DOCKER_VERSION=18.10.1 \
GITLAB_BACKUP_BUCKET_URI=s3://example-gitlab-backups/data \
GITLAB_BACKUP_S3CMD_BIN=s3cmd \
GITLAB_RESTORE_DRILL_DOCKER_BIN=docker \
GITLAB_LIVE_CONTAINER_NAMES=gitlab \
"$RESTORE_SCRIPT" --execute \
--target-container gitlab-restore-drill-ce \
--confirm-target gitlab-restore-drill-ce \
--source-edition ce \
--object "s3://example-gitlab-backups/data/${ce_name}" \
> "${test_dir}/ce-target.log" 2>&1 || {
  printf 'Expected real-format CE archive restore path to run:\n' >&2
  sed -n '1,200p' "${test_dir}/ce-target.log" >&2
  exit 1
}
assert_contains "$TEST_COMMAND_LOG" "gitlab-backup restore BACKUP=${ce_name%_gitlab_backup.tar} SKIP=registry"

grep -Fq 'OnFailure=gitlab-backup-alert@%n.service' \
  "${ROOT_DIR}/ops/gitlab/gitlab-data-backup.service"
grep -Fq 'TimeoutStartSec=' \
  "${ROOT_DIR}/ops/gitlab/gitlab-data-backup.service"
grep -Fq 'ExecStart=/usr/local/sbin/gitlab-restore-drill.sh --validate-only' \
  "${ROOT_DIR}/ops/gitlab/gitlab-restore-drill.service"
grep -Fq 'TimeoutStartSec=' \
  "${ROOT_DIR}/ops/gitlab/gitlab-restore-drill.service"
grep -Fq 'EnvironmentFile=/etc/gitlab-backup/alert.env' \
  "${ROOT_DIR}/ops/gitlab/gitlab-backup-alert@.service"
grep -Fq 'TimeoutStartSec=' \
  "${ROOT_DIR}/ops/gitlab/gitlab-backup-alert@.service"
grep -Fq -- '--connect-timeout 10' \
  "${ROOT_DIR}/ops/gitlab/gitlab-backup-alert.sh"
grep -Fq -- '--max-time 30' \
  "${ROOT_DIR}/ops/gitlab/gitlab-backup-alert.sh"
grep -Fq -- '--retry 3' \
  "${ROOT_DIR}/ops/gitlab/gitlab-backup-alert.sh"

printf 'gitlab backup tests: PASS\n'
