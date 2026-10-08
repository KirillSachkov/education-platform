#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_UNDER_TEST="$ROOT_DIR/scripts/validate-production-env.sh"
TEST_DIR="$(mktemp -d)"
trap 'rm -rf "$TEST_DIR"' EXIT

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

write_required() {
    local target="$1"
    local bot_value="${2-valid-bot-token}"

    {
        printf 'POSTGRES_USER=platform\n'
        printf 'POSTGRES_PASSWORD=valid-postgres-password\n'
        printf 'RABBITMQ_DEFAULT_USER=platform\n'
        printf 'RABBITMQ_DEFAULT_PASS=valid-rabbit-password\n'
        printf 'TYPESENSE_API_KEY=valid-typesense-key\n'
        printf 'BOT__TOKEN=%s\n' "$bot_value"
        printf 'INFISICAL_AUTH_SECRET=valid-auth-secret\n'
        printf 'INFISICAL_ENCRYPTION_KEY=valid-encryption-key\n'
    } > "$target"
}

baseline="$TEST_DIR/baseline.env"
candidate="$TEST_DIR/candidate.env"
write_required "$baseline"
printf 'LEGACY_OPTIONAL=\n' >> "$baseline"

write_required "$candidate" '"valid-bot-token"'
printf 'LEGACY_OPTIONAL=\n' >> "$candidate"
"$SCRIPT_UNDER_TEST" "$candidate" "$baseline" >/dev/null || fail "valid candidate was rejected"

for empty_value in '' '""' "''" '   ' '"   "'; do
    write_required "$candidate" "$empty_value"
    if "$SCRIPT_UNDER_TEST" "$candidate" "$baseline" >/dev/null 2>&1; then
        fail "empty BOT__TOKEN form was accepted"
    fi
done

write_required "$candidate"
printf 'BOT__TOKEN=duplicate\n' >> "$candidate"
if "$SCRIPT_UNDER_TEST" "$candidate" "$baseline" >/dev/null 2>&1; then
    fail "duplicate required key was accepted"
fi

write_required "$candidate"
if "$SCRIPT_UNDER_TEST" "$candidate" "$baseline" >/dev/null 2>&1; then
    fail "candidate that dropped a baseline key was accepted"
fi

printf 'Production env validator tests passed.\n'
