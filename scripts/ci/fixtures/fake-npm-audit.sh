#!/usr/bin/env bash

set -Eeuo pipefail

state_file="${FAKE_NPM_STATE:?FAKE_NPM_STATE must be set}"
attempt=0
if [[ -f "$state_file" ]]; then
    attempt="$(<"$state_file")"
fi
attempt=$((attempt + 1))
printf '%s\n' "$attempt" > "$state_file"
printf '%s\n' "$*" >> "${FAKE_NPM_ARGS_LOG:?FAKE_NPM_ARGS_LOG must be set}"

case "${FAKE_NPM_MODE:-success}" in
    success)
        printf 'found 0 vulnerabilities\n'
        ;;
    transient-then-success)
        if ((attempt <= ${FAKE_NPM_TRANSIENT_FAILURES:-1})); then
            printf 'npm warn audit request failed, reason: ETIMEDOUT\n' >&2
            printf 'npm error audit endpoint returned an error\n' >&2
            exit 1
        fi
        printf 'found 0 vulnerabilities\n'
        ;;
    persistent-transient)
        printf 'npm warn audit request failed, reason: ECONNRESET\n' >&2
        printf 'npm error audit endpoint returned an error\n' >&2
        exit 1
        ;;
    vulnerability)
        printf '1 moderate severity vulnerability\n' >&2
        exit 1
        ;;
    *)
        printf 'unknown fake mode\n' >&2
        exit 2
        ;;
esac
