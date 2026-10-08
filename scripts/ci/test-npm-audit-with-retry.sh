#!/usr/bin/env bash

set -Eeuo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
SCRIPT_UNDER_TEST="$ROOT_DIR/scripts/ci/npm-audit-with-retry.sh"
FAKE_NPM="$ROOT_DIR/scripts/ci/fixtures/fake-npm-audit.sh"

fail() {
    printf 'FAIL: %s\n' "$1" >&2
    exit 1
}

[[ -f "$SCRIPT_UNDER_TEST" ]] || fail "missing $SCRIPT_UNDER_TEST"

github_job="$(awk '/^  admin:/ { capture = 1 } capture && /^  frontend:/ { exit } capture { print }' "$ROOT_DIR/.github/workflows/ci.yml")"
[[ "$github_job" == *'bash ../../scripts/ci/npm-audit-with-retry.sh --audit-level=moderate'* ]] ||
    fail "GitHub admin check does not invoke the audit wrapper"
python3 - "$ROOT_DIR" <<'PY'
import json, sys
from pathlib import Path
paths = json.loads((Path(sys.argv[1]) / 'scripts/ci/github-ci-paths.json').read_text())
assert 'scripts/ci/npm-audit-with-retry.sh' in paths['mandatory']['admin'], 'audit wrapper must select the actual GitHub admin check'
PY

temp_dir="$(mktemp -d)"
trap 'rm -rf "$temp_dir"' EXIT
state_file="$temp_dir/state"
args_log="$temp_dir/args"

FAKE_NPM_STATE="$state_file" \
FAKE_NPM_ARGS_LOG="$args_log" \
FAKE_NPM_MODE="transient-then-success" \
FAKE_NPM_TRANSIENT_FAILURES=2 \
NPM_BIN="$FAKE_NPM" \
NPM_AUDIT_MAX_ATTEMPTS=3 \
NPM_AUDIT_RETRY_DELAY_SECONDS=0 \
    bash "$SCRIPT_UNDER_TEST" --audit-level=moderate >/dev/null 2>&1
[[ "$(<"$state_file")" == "3" ]] || fail "transient audit failure was not retried"
[[ "$(wc -l < "$args_log" | tr -d ' ')" == "3" ]] || fail "unexpected transient retry count"
if grep -v '^audit --audit-level=moderate$' "$args_log" >/dev/null; then
    fail "npm audit arguments changed during retry"
fi

: > "$state_file"
: > "$args_log"
if FAKE_NPM_STATE="$state_file" \
    FAKE_NPM_ARGS_LOG="$args_log" \
    FAKE_NPM_MODE="persistent-transient" \
    NPM_BIN="$FAKE_NPM" \
    NPM_AUDIT_MAX_ATTEMPTS=3 \
    NPM_AUDIT_RETRY_DELAY_SECONDS=0 \
        bash "$SCRIPT_UNDER_TEST" --audit-level=moderate >/dev/null 2>&1; then
    fail "persistent audit endpoint failure was swallowed"
fi
[[ "$(<"$state_file")" == "3" ]] || fail "persistent failure ignored the retry bound"

: > "$state_file"
: > "$args_log"
if FAKE_NPM_STATE="$state_file" \
    FAKE_NPM_ARGS_LOG="$args_log" \
    FAKE_NPM_MODE="vulnerability" \
    NPM_BIN="$FAKE_NPM" \
    NPM_AUDIT_MAX_ATTEMPTS=3 \
    NPM_AUDIT_RETRY_DELAY_SECONDS=0 \
        bash "$SCRIPT_UNDER_TEST" --audit-level=moderate >/dev/null 2>&1; then
    fail "vulnerability finding was swallowed"
fi
[[ "$(<"$state_file")" == "1" ]] || fail "deterministic vulnerability failure was retried"

printf 'npm audit retry tests passed.\n'
