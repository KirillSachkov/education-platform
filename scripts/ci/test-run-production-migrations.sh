#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_UNDER_TEST="$ROOT_DIR/scripts/run-production-migrations.sh"
FAKE_DOCKER="$ROOT_DIR/scripts/ci/fixtures/fake-docker-compose.sh"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

[[ -f "$SCRIPT_UNDER_TEST" ]] || fail "missing $SCRIPT_UNDER_TEST"

log_file="$(mktemp)"
temp_dir="$(mktemp -d)"
current_release="$temp_dir/current.env"
missing_release="$temp_dir/missing.env"
printf 'MEDIA_BINDING_PROTOCOL=1\n' > "$current_release"
trap 'rm -f "$log_file"; rm -rf "$temp_dir"' EXIT

expected_services=$'auth-service-migrations\neducation-service-migrations\nfile-service-migrations\nprogress-service-migrations\ncomment-service-migrations\naccess-service-migrations\nnotification-service-migrations\ntelegram-bot-service-migrations\nassignment-review-service-migrations'

FAKE_DOCKER_LOG="$log_file" \
DOCKER_BIN="$FAKE_DOCKER" \
    bash "$SCRIPT_UNDER_TEST" docker-compose.test.yml "$current_release" >/dev/null

actual_services="$(awk '{print $NF}' "$log_file")"
[[ "$actual_services" == "$expected_services" ]] ||
    fail "successful preflight did not run every migration service in order"

if grep -vE '^compose -f docker-compose\.test\.yml run --rm --no-deps --no-TTY [a-z-]+-migrations$' \
    "$log_file" >/dev/null; then
    fail "unexpected docker compose arguments"
fi

: > "$log_file"
if FAKE_DOCKER_LOG="$log_file" \
    FAKE_DOCKER_FAIL_SERVICE="comment-service-migrations" \
    DOCKER_BIN="$FAKE_DOCKER" \
        bash "$SCRIPT_UNDER_TEST" docker-compose.test.yml "$current_release" >/dev/null 2>&1; then
    fail "migration failure was swallowed"
fi

expected_before_failure=$'auth-service-migrations\neducation-service-migrations\nfile-service-migrations\nprogress-service-migrations\ncomment-service-migrations'
actual_before_failure="$(awk '{print $NF}' "$log_file")"
[[ "$actual_before_failure" == "$expected_before_failure" ]] ||
    fail "preflight did not stop immediately after the failed migration"

: > "$log_file"
FAKE_DOCKER_LOG="$log_file" \
DOCKER_BIN="$FAKE_DOCKER" \
    bash "$SCRIPT_UNDER_TEST" docker-compose.test.yml "$missing_release" >/dev/null

expected_first_cutover=$'compose -f docker-compose.test.yml stop file-service\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY auth-service-migrations\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY education-service-migrations\ncompose -f docker-compose.test.yml up -d --force-recreate --no-deps --wait --wait-timeout 180 auth-service education-service\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY file-service-migrations\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY progress-service-migrations\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY comment-service-migrations\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY access-service-migrations\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY notification-service-migrations\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY telegram-bot-service-migrations\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY assignment-review-service-migrations'
actual_first_cutover="$(<"$log_file")"
[[ "$actual_first_cutover" == "$expected_first_cutover" ]] ||
    fail "first media-binding cutover order is unsafe"

: > "$log_file"
if FAKE_DOCKER_LOG="$log_file" \
    FAKE_DOCKER_FAIL_SERVICE="auth-service-migrations" \
    DOCKER_BIN="$FAKE_DOCKER" \
        bash "$SCRIPT_UNDER_TEST" docker-compose.test.yml "$missing_release" >/dev/null 2>&1; then
    fail "first-cutover Auth migration failure was swallowed"
fi

expected_failed_cutover=$'compose -f docker-compose.test.yml stop file-service\ncompose -f docker-compose.test.yml run --rm --no-deps --no-TTY auth-service-migrations'
actual_failed_cutover="$(<"$log_file")"
[[ "$actual_failed_cutover" == "$expected_failed_cutover" ]] ||
    fail "first cutover continued after the failed Auth migration"

# Frozen legacy roles still use the original complete migration inventory.
: > "$log_file"
FAKE_DOCKER_LOG="$log_file" DOCKER_BIN="$FAKE_DOCKER" \
    bash "$SCRIPT_UNDER_TEST" docker-compose.test.yml "$current_release" legacy >/dev/null
legacy_services="$(awk '{print $NF}' "$log_file")"
expected_legacy=$'auth-service-migrations
education-service-migrations
file-service-migrations
progress-service-migrations
comment-service-migrations
tag-service-migrations
search-service-migrations
access-service-migrations
material-processing-service-migrations
notification-service-migrations
telegram-bot-service-migrations
trainer-service-migrations
assignment-review-service-migrations'
[[ "$legacy_services" == "$expected_legacy" ]] || fail "legacy migration inventory changed"

: > "$log_file"
if FAKE_DOCKER_LOG="$log_file" DOCKER_BIN="$FAKE_DOCKER" \
    bash "$SCRIPT_UNDER_TEST" docker-compose.test.yml "$current_release" unknown >/dev/null 2>&1; then
    fail "unknown topology was accepted"
fi
[[ ! -s "$log_file" ]] || fail "unknown topology reached Docker"

printf 'Production migration preflight tests passed.\n'
