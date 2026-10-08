#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_UNDER_TEST="$ROOT_DIR/scripts/restore-s3.sh"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

assert_contains() {
    local file="$1"
    local expected="$2"

    grep -F -- "$expected" "$file" >/dev/null ||
        fail "expected '$expected' in $file"
}

assert_not_contains() {
    local file="$1"
    local unexpected="$2"

    if grep -F -- "$unexpected" "$file" >/dev/null; then
        fail "did not expect '$unexpected' in $file"
    fi
}

TEMP_ROOT="$(mktemp -d)"
trap 'rm -rf "$TEMP_ROOT"' EXIT

FAKE_BIN="$TEMP_ROOT/bin"
mkdir -p "$FAKE_BIN"

cat > "$FAKE_BIN/aws" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail

printf '%s\n' "$*" >> "$FAKE_AWS_LOG"

if [[ "${1:-} ${2:-}" == "s3 ls" ]]; then
    cat <<'LIST'
2026-07-15 03:00:00      12000 backups/backup_20260715_030000.sql.gz
2026-07-16 02:00:00        100 backups/readme.txt
2026-07-16 03:00:00      13000 backups/backup_20260716_030000.sql.gz
LIST
    exit 0
fi

if [[ "${1:-} ${2:-}" == "s3 cp" ]]; then
    cp "$FAKE_BACKUP_ARCHIVE" "$4"
    exit 0
fi

printf 'unexpected aws invocation: %s\n' "$*" >&2
exit 64
EOF

cat > "$FAKE_BIN/docker" <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail

printf '%s\n' "$*" >> "$FAKE_DOCKER_LOG"

case "${1:-}" in
    volume)
        case "${2:-}" in
            create)
                printf '%s\n' "${3:-restore-volume}"
                ;;
            rm)
                ;;
            *)
                exit 64
                ;;
        esac
        ;;
    run)
        printf '%s\n' fake-container-id
        ;;
    exec)
        if [[ "$*" == *"pg_isready"* ]]; then
            exit 0
        fi

        if [[ "$*" == *"psql"* && "$*" != *" -c "* ]]; then
            cat > "$FAKE_RESTORE_SQL"
            [[ "${FAKE_DOCKER_FAIL_RESTORE:-0}" != "1" ]] || exit 23
            exit 0
        fi

        case "$*" in
            *"SELECT 1 FROM pg_database"*)
                printf '1\n'
                ;;
            *"FROM pg_namespace"*)
                printf '%s\n' "${FAKE_SCHEMA_COUNT:-13}"
                ;;
            *"FROM pg_tables"*)
                printf '%s\n' "${FAKE_TABLE_COUNT:-232}"
                ;;
            *"auth.users"*"education.courses"*"access.plan_grants"*)
                printf 'auth.users|42\neducation.courses|8\naccess.plan_grants|17\n'
                ;;
            *"GRANT pg_monitor TO postgres_exporter;"*)
                ;;
            *)
                printf 'unexpected docker exec invocation: %s\n' "$*" >&2
                exit 64
                ;;
        esac
        ;;
    rm)
        ;;
    *)
        printf 'unexpected docker invocation: %s\n' "$*" >&2
        exit 64
        ;;
esac
EOF

chmod +x "$FAKE_BIN/aws" "$FAKE_BIN/docker"

FIXTURE_SQL="$TEMP_ROOT/fixture.sql"
KNOWN_GRANT='GRANT pg_monitor TO postgres_exporter WITH INHERIT TRUE GRANTED BY platform;'
cat > "$FIXTURE_SQL" <<EOF
CREATE ROLE platform;
CREATE ROLE postgres_exporter;
$KNOWN_GRANT
GRANT pg_monitor TO postgres_exporter;
GRANT pg_read_all_stats TO postgres_exporter;
EOF
gzip -c "$FIXTURE_SQL" > "$TEMP_ROOT/fixture.sql.gz"
printf 'not a gzip archive\n' > "$TEMP_ROOT/corrupt.sql.gz"

CASE_DIR="$TEMP_ROOT/case"
AWS_LOG="$CASE_DIR/aws.log"
DOCKER_LOG="$CASE_DIR/docker.log"
RESTORE_SQL="$CASE_DIR/restored.sql"
OUTPUT="$CASE_DIR/output.log"
WORK_DIR="$CASE_DIR/work"

reset_case() {
    rm -rf "$CASE_DIR"
    mkdir -p "$WORK_DIR"
    : > "$AWS_LOG"
    : > "$DOCKER_LOG"
    : > "$OUTPUT"
}

run_restore() {
    env \
        PATH="$FAKE_BIN:$PATH" \
        TMPDIR="$WORK_DIR" \
        FAKE_AWS_LOG="$AWS_LOG" \
        FAKE_DOCKER_LOG="$DOCKER_LOG" \
        FAKE_RESTORE_SQL="$RESTORE_SQL" \
        FAKE_BACKUP_ARCHIVE="${FAKE_BACKUP_ARCHIVE:-$TEMP_ROOT/fixture.sql.gz}" \
        FAKE_DOCKER_FAIL_RESTORE="${FAKE_DOCKER_FAIL_RESTORE:-0}" \
        FAKE_SCHEMA_COUNT="${FAKE_SCHEMA_COUNT:-13}" \
        FAKE_TABLE_COUNT="${FAKE_TABLE_COUNT:-232}" \
        BACKUP_S3_BUCKET=test-bucket \
        BACKUP_S3_ENDPOINT=https://s3.test.invalid \
        BACKUP_S3_ACCESS_KEY=test-access \
        BACKUP_S3_SECRET_KEY=test-secret \
        RESTORE_POSTGRES_IMAGE="${FAKE_POSTGRES_IMAGE:-pgvector/pgvector:pg16}" \
        bash "$SCRIPT_UNDER_TEST" "$@" > "$OUTPUT" 2>&1
}

reset_case
bash "$SCRIPT_UNDER_TEST" --help > "$OUTPUT" 2>&1 || fail "--help failed"
assert_contains "$OUTPUT" "--drill"
assert_contains "$OUTPUT" "--latest"
assert_contains "$OUTPUT" "--keep"

reset_case
if run_restore --latest; then
    fail "restore without --drill was not rejected"
fi
assert_contains "$OUTPUT" "--drill"
[[ ! -s "$DOCKER_LOG" ]] || fail "unsafe invocation reached Docker"

reset_case
run_restore --drill --latest || fail "latest restore drill failed"
assert_contains "$AWS_LOG" "s3 cp s3://test-bucket/backups/backup_20260716_030000.sql.gz"
assert_not_contains "$RESTORE_SQL" "$KNOWN_GRANT"
assert_contains "$RESTORE_SQL" "GRANT pg_monitor TO postgres_exporter;"
assert_contains "$RESTORE_SQL" "GRANT pg_read_all_stats TO postgres_exporter;"
assert_contains "$DOCKER_LOG" "psql -v ON_ERROR_STOP=1 -U postgres -d postgres"
assert_contains "$DOCKER_LOG" "GRANT pg_monitor TO postgres_exporter;"
assert_contains "$DOCKER_LOG" "run -d --name"
assert_contains "$DOCKER_LOG" "--network none"
assert_contains "$DOCKER_LOG" "pgvector/pgvector:pg16"
assert_contains "$DOCKER_LOG" "rm -f"
assert_contains "$DOCKER_LOG" "volume rm"
if find "$WORK_DIR" -mindepth 1 -print -quit | grep -q .; then
    fail "temporary restore workspace was not cleaned"
fi

reset_case
run_restore --drill backups/backup_20260715_030000.sql.gz || fail "explicit object restore drill failed"
assert_contains "$AWS_LOG" "s3 cp s3://test-bucket/backups/backup_20260715_030000.sql.gz"

reset_case
FAKE_POSTGRES_IMAGE=gitlab-sachkov.ru:5050/retired/postgres:pg16
if run_restore --drill --latest; then
    fail "retired registry image was accepted"
fi
unset FAKE_POSTGRES_IMAGE
[[ ! -s "$AWS_LOG" && ! -s "$DOCKER_LOG" ]] || fail "retired image reached AWS or Docker"

reset_case
if env BACKUP_S3_ACCESS_KEY=test BACKUP_S3_SECRET_KEY=test RESTORE_POSTGRES_IMAGE= \
    bash "$SCRIPT_UNDER_TEST" --drill test.sql.gz > "$OUTPUT" 2>&1; then
    fail "missing PostgreSQL image was accepted"
fi
assert_contains "$OUTPUT" "RESTORE_POSTGRES_IMAGE must be set"

reset_case
FAKE_BACKUP_ARCHIVE="$TEMP_ROOT/corrupt.sql.gz"
if run_restore --drill --latest; then
    fail "corrupt gzip archive was accepted"
fi
unset FAKE_BACKUP_ARCHIVE
assert_not_contains "$DOCKER_LOG" "volume create"
assert_contains "$OUTPUT" "gzip integrity"

reset_case
FAKE_DOCKER_FAIL_RESTORE=1
if run_restore --drill --latest; then
    fail "failed psql restore was accepted"
fi
unset FAKE_DOCKER_FAIL_RESTORE
assert_contains "$DOCKER_LOG" "rm -f"
assert_contains "$DOCKER_LOG" "volume rm"

reset_case
FAKE_SCHEMA_COUNT=12
if run_restore --drill --latest; then
    fail "incomplete application schemas were accepted"
fi
unset FAKE_SCHEMA_COUNT
assert_contains "$OUTPUT" "application schema verification failed"
assert_contains "$DOCKER_LOG" "rm -f"
assert_contains "$DOCKER_LOG" "volume rm"

reset_case
run_restore --drill --latest --keep || fail "--keep restore drill failed"
assert_not_contains "$DOCKER_LOG" "rm -f"
assert_not_contains "$DOCKER_LOG" "volume rm"
assert_contains "$OUTPUT" "Keeping drill resources"
if ! find "$WORK_DIR" -mindepth 1 -print -quit | grep -q .; then
    fail "--keep removed the temporary restore workspace"
fi

printf 'PostgreSQL S3 restore tests passed.\n'
