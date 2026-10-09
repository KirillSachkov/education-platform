#!/usr/bin/env bash

set -Eeuo pipefail

CANDIDATE_ENV="${1:-}"
BASELINE_ENV="${2:-}"

fail() {
    printf 'ERROR: production env validation failed: %s\n' "$*" >&2
    exit 1
}

extract_keys() {
    awk '
        /^[[:space:]]*(#|$)/ { next }
        {
            separator = index($0, "=")
            if (separator == 0) next
            key = substr($0, 1, separator - 1)
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", key)
            if (key ~ /^[A-Za-z_][A-Za-z0-9_]*$/) print key
        }
    ' "$1"
}

[[ -f "$CANDIDATE_ENV" && -s "$CANDIDATE_ENV" ]] ||
    fail "candidate env file is missing or empty"

duplicate_key="$(extract_keys "$CANDIDATE_ENV" | LC_ALL=C sort | uniq -d |
    awk 'NR == 1 { value = $0 } END { print value }')"
[[ -z "$duplicate_key" ]] || fail "candidate contains duplicate key: ${duplicate_key}"

critical_keys=(
    POSTGRES_USER
    POSTGRES_PASSWORD
    RABBITMQ_DEFAULT_USER
    RABBITMQ_DEFAULT_PASS
    BOT__TOKEN
    INFISICAL_AUTH_SECRET
    INFISICAL_ENCRYPTION_KEY
)

for required_key in "${critical_keys[@]}"; do
    if ! raw_value="$(awk -v wanted="$required_key" '
        {
            separator = index($0, "=")
            if (separator == 0) next
            key = substr($0, 1, separator - 1)
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", key)
            if (key == wanted) {
                count += 1
                value = substr($0, separator + 1)
            }
        }
        END {
            if (count != 1) exit 2
            printf "%s", value
        }
    ' "$CANDIDATE_ENV")"; then
        fail "candidate is missing required secret: ${required_key}"
    fi

    normalized_value="${raw_value//[[:space:]]/}"
    case "$normalized_value" in
        '' | '""' | "''")
            fail "candidate has an empty required secret: ${required_key}"
            ;;
    esac
done

if [[ -n "$BASELINE_ENV" && -f "$BASELINE_ENV" ]]; then
    missing_keys="$(comm -23 \
        <(extract_keys "$BASELINE_ENV" | LC_ALL=C sort -u) \
        <(extract_keys "$CANDIDATE_ENV" | LC_ALL=C sort -u))"
    if [[ -n "$missing_keys" ]]; then
        missing_keys="${missing_keys//$'\n'/ }"
        fail "candidate dropped keys present in the live env: ${missing_keys}"
    fi
fi

printf 'Production env validation passed.\n'
