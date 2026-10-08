#!/usr/bin/env bash

set -Eeuo pipefail

npm_bin="${NPM_BIN:-npm}"
max_attempts="${NPM_AUDIT_MAX_ATTEMPTS:-3}"
retry_delay_seconds="${NPM_AUDIT_RETRY_DELAY_SECONDS:-10}"

[[ "$max_attempts" =~ ^[1-9][0-9]*$ ]] || {
    printf 'NPM_AUDIT_MAX_ATTEMPTS must be a positive integer.\n' >&2
    exit 2
}
[[ "$retry_delay_seconds" =~ ^[0-9]+$ ]] || {
    printf 'NPM_AUDIT_RETRY_DELAY_SECONDS must be a non-negative integer.\n' >&2
    exit 2
}

audit_log="$(mktemp)"
trap 'rm -f "$audit_log"' EXIT

for ((attempt = 1; attempt <= max_attempts; attempt++)); do
    set +e
    "$npm_bin" audit "$@" > "$audit_log" 2>&1
    status=$?
    set -e

    cat "$audit_log"
    if ((status == 0)); then
        exit 0
    fi

    if ! grep -Eiq \
        'audit (request .* failed|endpoint returned an error)|EAI_AGAIN|ECONNRESET|ETIMEDOUT|ECONNREFUSED|ENETUNREACH|fetch failed' \
        "$audit_log"; then
        exit "$status"
    fi

    if ((attempt == max_attempts)); then
        printf 'npm audit endpoint failed after %s attempts.\n' "$max_attempts" >&2
        exit "$status"
    fi

    printf 'npm audit endpoint unavailable; retrying attempt %s/%s in %ss.\n' \
        "$((attempt + 1))" "$max_attempts" "$retry_delay_seconds" >&2
    sleep "$retry_delay_seconds"
done
