#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
GUARD_UNDER_TEST="$ROOT_DIR/scripts/check-production-rollback.sh"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

[[ -f "$GUARD_UNDER_TEST" ]] || fail "missing $GUARD_UNDER_TEST"

rollback_job="$(awk '
    /^rollback-production:/ { capture = 1 }
    capture && /^  rules:/ { exit }
    capture { print }
' "$ROOT_DIR/.gitlab-ci.yml")"
[[ "$rollback_job" == *'/opt/education-platform/scripts/check-production-rollback.sh'* ]] ||
    fail "rollback-production does not invoke the protocol guard"
[[ "$rollback_job" != *'#347 guard'* ]] ||
    fail "rollback-production still contains the fail-open recovery block"

compose_up_line="$(grep -nF 'docker compose -f docker-compose.prod.yml up -d --force-recreate &&' <<<"$rollback_job" | cut -d: -f1)"
metadata_swap_line="$(grep -nF "mv \\\$RELEASES_DIR/current.env \\\$RELEASES_DIR/rolled-back-from.env &&" <<<"$rollback_job" | cut -d: -f1)"
((compose_up_line < metadata_swap_line)) ||
    fail "rollback metadata is changed before compose startup succeeds"

temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT
current="$temp_dir/current.env"
previous="$temp_dir/previous.env"
pending="$temp_dir/pending.env"

printf 'IMAGE_TAG=current\n' > "$current"
printf 'IMAGE_TAG=previous\n' > "$previous"
printf 'IMAGE_TAG=target\nMEDIA_BINDING_PROTOCOL=1\n' > "$pending"
if bash "$GUARD_UNDER_TEST" "$current" "$previous" "$pending" >/dev/null 2>&1; then
    fail "partial protocol cutover did not block rollback"
fi

printf 'IMAGE_TAG=current\nMEDIA_BINDING_PROTOCOL=1\n' > "$current"
rm -f "$pending"
if bash "$GUARD_UNDER_TEST" "$current" "$previous" "$pending" >/dev/null 2>&1; then
    fail "completed protocol cutover did not block rollback to protocol 0"
fi

printf 'IMAGE_TAG=previous\nMEDIA_BINDING_PROTOCOL=1\n' > "$previous"
bash "$GUARD_UNDER_TEST" "$current" "$previous" "$pending" >/dev/null

printf 'IMAGE_TAG=current\n' > "$current"
printf 'IMAGE_TAG=previous\n' > "$previous"
bash "$GUARD_UNDER_TEST" "$current" "$previous" "$pending" >/dev/null

printf 'Production rollback guard tests passed.\n'
